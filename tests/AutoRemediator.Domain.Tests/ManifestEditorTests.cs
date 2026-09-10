using AutoRemediator.Domain;

namespace AutoRemediator.Domain.Tests;

public class ManifestEditorTests
{
    [Fact]
    public void Updates_central_package_management_version()
    {
        const string props = """
            <Project>
              <ItemGroup>
                <PackageVersion Include="Contoso.Core" Version="1.0.0" />
                <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """;

        var result = ManifestEditor.SetVersion(props, "Contoso.Core", "2.0.0");

        Assert.Contains("""<PackageVersion Include="Contoso.Core" Version="2.0.0" />""", result);
        // Unrelated package untouched.
        Assert.Contains("""<PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />""", result);
    }

    [Fact]
    public void Updates_packagereference_child_element_version()
    {
        const string csproj = """
            <Project>
              <ItemGroup>
                <PackageReference Include="Contoso.Data">
                  <Version>1.2.3</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """;

        var result = ManifestEditor.SetVersion(csproj, "Contoso.Data", "1.3.0");

        Assert.Contains("<Version>1.3.0</Version>", result);
        Assert.DoesNotContain("1.2.3", result);
    }

    [Fact]
    public void Absent_package_returns_content_unchanged()
    {
        const string props = """<Project><ItemGroup><PackageVersion Include="Contoso.Core" Version="1.0.0" /></ItemGroup></Project>""";

        var result = ManifestEditor.SetVersion(props, "Contoso.Missing", "9.9.9");

        Assert.Equal(props, result);
    }

    [Fact]
    public void Only_the_targeted_package_changes_when_ids_share_a_prefix()
    {
        const string props = """
            <Project><ItemGroup>
              <PackageVersion Include="Contoso.Core" Version="1.0.0" />
              <PackageVersion Include="Contoso.Core.Extensions" Version="1.0.0" />
            </ItemGroup></Project>
            """;

        var result = ManifestEditor.SetVersion(props, "Contoso.Core", "2.0.0");

        Assert.Contains("""<PackageVersion Include="Contoso.Core" Version="2.0.0" />""", result);
        Assert.Contains("""<PackageVersion Include="Contoso.Core.Extensions" Version="1.0.0" />""", result);
    }
}
