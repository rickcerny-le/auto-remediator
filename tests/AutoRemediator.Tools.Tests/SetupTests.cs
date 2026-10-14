using LocalScenarioSetup;

namespace AutoRemediator.Tools.Tests;

public sealed class ToolLocatorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tool-locator-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Touch(string file)
    {
        var path = Path.Combine(_dir, file);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    [Fact]
    public void Finds_the_tool_in_a_path_entry()
    {
        var file = Touch(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");
        var path = string.Join(Path.PathSeparator, Path.Combine(_dir, "missing"), _dir);

        var found = ToolLocator.Find("pwsh", path, ".COM;.EXE;.BAT;.CMD");

        Assert.Equal(file, found, ignoreCase: OperatingSystem.IsWindows());
    }

    [Fact]
    public void Returns_null_when_absent()
    {
        Touch("other.exe");

        Assert.Null(ToolLocator.Find("pwsh", _dir, ".EXE"));
        Assert.Null(ToolLocator.Find("pwsh", null, ".EXE"));
        Assert.Null(ToolLocator.Find("pwsh", string.Empty, ".EXE"));
    }

    [Fact]
    public void Honours_pathext_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Touch("pwsh.cmd");

        Assert.NotNull(ToolLocator.Find("pwsh", _dir, ".EXE;.CMD"));
        Assert.Null(ToolLocator.Find("pwsh", _dir, ".EXE;.BAT"));
    }

    [Fact]
    public void First_path_entry_wins()
    {
        var other = Directory.CreateTempSubdirectory("tool-locator-b-").FullName;
        try
        {
            var name = OperatingSystem.IsWindows() ? "git.exe" : "git";
            var first = Touch(name);
            File.WriteAllText(Path.Combine(other, name), string.Empty);

            var found = ToolLocator.Find("git", string.Join(Path.PathSeparator, _dir, other), ".EXE");

            Assert.Equal(first, found, ignoreCase: OperatingSystem.IsWindows());
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }
}

public sealed class PreflightTests
{
    private static Func<string, string?> Having(params string[] present) =>
        tool => present.Contains(tool) ? "/bin/" + tool : null;

    [Fact]
    public void Provision_needs_pwsh_git_and_dotnet()
    {
        Assert.Empty(Preflight.Check("provision", Having("pwsh", "git", "dotnet")));
        Assert.Equal(["pwsh", "git", "dotnet"], Preflight.Check("provision", Having()).Select(m => m.Tool));
        Assert.Equal(["dotnet"], Preflight.Check("provision", Having("pwsh", "git")).Select(m => m.Tool));
    }

    [Fact]
    public void Reset_needs_pwsh_and_git_only()
    {
        Assert.Empty(Preflight.Check("reset", Having("pwsh", "git")));
        Assert.Equal(["pwsh"], Preflight.Check("reset", Having("git")).Select(m => m.Tool));
        Assert.Equal(["git"], Preflight.Check("reset", Having("pwsh")).Select(m => m.Tool));
    }

    [Fact]
    public void Each_missing_tool_carries_a_one_line_hint()
    {
        var missing = Preflight.Check("provision", Having());

        Assert.All(missing, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Hint));
            Assert.DoesNotContain('\n', m.Hint);
        });
        Assert.Contains("https://aka.ms/powershell", missing.Single(m => m.Tool == "pwsh").Hint);
        Assert.Contains("git is required", missing.Single(m => m.Tool == "git").Hint);
        Assert.Contains(".NET 10 SDK", missing.Single(m => m.Tool == "dotnet").Hint);
    }
}

public sealed class ScriptCommandTests
{
    private const string Scripts = "C:\\my tools\\local scenarios";
    private const string Root = "C:\\my repo\\.local";

    private static readonly string Script = Path.Combine(Scripts, "New-LocalScenarios.ps1");

    [Fact]
    public void Provision_passes_reset_if_current()
    {
        Assert.Equal(
            ["-NoProfile", "-NonInteractive", "-File", Script, "-Root", Root, "-ResetIfCurrent"],
            ScriptCommand.For("provision", Scripts, Root, noReset: false));
    }

    [Fact]
    public void Provision_with_no_reset_omits_the_switch()
    {
        Assert.Equal(
            ["-NoProfile", "-NonInteractive", "-File", Script, "-Root", Root],
            ScriptCommand.For("provision", Scripts, Root, noReset: true));
    }

    [Fact]
    public void Reset_passes_reset()
    {
        Assert.Equal(
            ["-NoProfile", "-NonInteractive", "-File", Script, "-Root", Root, "-Reset"],
            ScriptCommand.For("reset", Scripts, Root, noReset: false));
    }

    [Fact]
    public void Paths_with_spaces_are_single_unquoted_items()
    {
        var args = ScriptCommand.For("reset", Scripts, Root, noReset: false);

        Assert.Contains(Root, args);
        Assert.DoesNotContain(args, a => a.Contains('"'));
    }
}

public sealed class EnrollmentTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] vars) =>
        name => vars.Where(v => v.Name == name).Select(v => v.Value).FirstOrDefault();

    [Fact]
    public void Http_is_preferred_over_https()
    {
        var url = Enrollment.ResolveApiUrl(Env(
            ("services__api__https__0", "https://localhost:1"),
            ("services__api__http__0", "http://localhost:2/")));

        Assert.Equal("http://localhost:2", url);
    }

    [Fact]
    public void Https_is_the_fallback()
    {
        Assert.Equal(
            "https://localhost:1",
            Enrollment.ResolveApiUrl(Env(("services__api__https__0", "https://localhost:1"))));
    }

    [Fact]
    public void No_injected_url_resolves_to_null_with_a_clear_message()
    {
        Assert.Null(Enrollment.ResolveApiUrl(Env()));
        Assert.Null(Enrollment.ResolveApiUrl(Env(("services__api__http__0", "  "))));
        Assert.Contains("local-scenarios-enroll", Enrollment.NoApiUrlMessage);
    }

    [Theory]
    [InlineData("baseline")]
    [InlineData("patch")]
    [InlineData("major")]
    [InlineData("prerelease")]
    public void Valid_profiles_pass(string profile) => Assert.Null(Enrollment.ValidateProfile(profile));

    [Theory]
    [InlineData("bogus")]
    [InlineData("Major")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_profiles_fail_with_the_valid_values(string? profile)
    {
        Assert.Equal(
            $"LocalRepos__Profile '{profile}' is not valid; use baseline, patch, major or prerelease.",
            Enrollment.ValidateProfile(profile));
    }

    [Fact]
    public void Enroll_needs_only_pwsh()
    {
        Assert.Equal(["pwsh"], Preflight.RequiredTools("enroll"));
    }

    [Fact]
    public void Enroll_runs_the_config_script_with_the_resolved_arguments()
    {
        const string scripts = "C:\\my tools\\local scenarios";
        const string root = "C:\\my repo\\.local";

        Assert.Equal(
            [
                "-NoProfile", "-NonInteractive", "-File", Path.Combine(scripts, "Set-LocalScenarioConfig.ps1"),
                "-ApiUrl", "http://localhost:2", "-Profile", "major", "-Root", root,
            ],
            ScriptCommand.ForEnroll(scripts, root, "major", "http://localhost:2"));
    }
}

public sealed class OptionsTests
{
    [Fact]
    public void Parses_a_valid_command_line()
    {
        var o = Options.Parse(["provision", "--scripts", "s", "--root", "r", "--no-reset"], out var error);

        Assert.Null(error);
        Assert.Equal(new Options("provision", "s", "r", true), o);
    }

    [Theory]
    [InlineData("enroll", "--scripts", "s", "--root", "r")]
    [InlineData("bogus", "--scripts", "s", "--root", "r")]
    [InlineData("reset", "--root", "r")]
    [InlineData("reset", "--scripts", "s")]
    [InlineData("reset", "--scripts", "s", "--root")]
    [InlineData("reset", "--scripts", "s", "--root", "r", "--wat")]
    [InlineData("reset", "--scripts", "s", "--root", "r", "--no-reset")]
    public void Rejects_invalid_command_lines(params string[] args)
    {
        Assert.Null(Options.Parse(args, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Parses_enroll_with_a_profile()
    {
        var o = Options.Parse(["enroll", "--scripts", "s", "--root", "r", "--profile", "major"], out var error);

        Assert.Null(error);
        Assert.Equal(new Options("enroll", "s", "r", false, "major"), o);
    }

    [Theory]
    [InlineData("reset", "--scripts", "s", "--root", "r", "--profile", "major")]
    [InlineData("enroll", "--scripts", "s", "--root", "r", "--profile")]
    [InlineData("enroll", "--scripts", "s", "--root", "r", "--no-reset", "--profile", "major")]
    public void Rejects_misplaced_profile_arguments(params string[] args)
    {
        Assert.Null(Options.Parse(args, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Rejects_an_empty_command_line()
    {
        Assert.Null(Options.Parse([], out var error));
        Assert.NotNull(error);
    }
}
