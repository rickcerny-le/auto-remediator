using AutoRemediator.Domain;
using AutoRemediator.Infrastructure.Analysis;
using NuGet.Versioning;

namespace AutoRemediator.Infrastructure.Tests;

public class VersionPolicyTests
{
    private static NuGetVersion V(string s) => NuGetVersion.Parse(s);

    private static IEnumerable<NuGetVersion> Available(params string[] versions) => versions.Select(V);

    [Theory]
    [InlineData(UpdateStrategy.Patch, "1.4.3")]
    [InlineData(UpdateStrategy.Minor, "1.5.0")]
    [InlineData(UpdateStrategy.Major, "2.1.0")]
    public void Selects_highest_within_strategy_relative_to_current(UpdateStrategy strategy, string expected)
    {
        var target = VersionPolicy.SelectTarget(V("1.4.2"), Available("1.4.3", "1.5.0", "2.1.0"), strategy);

        Assert.Equal(expected, target?.ToNormalizedString());
    }

    [Fact]
    public void Patch_stays_within_major_minor()
    {
        var target = VersionPolicy.SelectTarget(V("1.4.2"), Available("1.4.9", "1.5.0"), UpdateStrategy.Patch);

        Assert.Equal("1.4.9", target?.ToNormalizedString());
    }

    [Fact]
    public void Minor_stays_within_major()
    {
        var target = VersionPolicy.SelectTarget(V("1.4.2"), Available("1.9.9", "2.0.0"), UpdateStrategy.Minor);

        Assert.Equal("1.9.9", target?.ToNormalizedString());
    }

    [Fact]
    public void No_higher_version_within_band_returns_null()
    {
        // current 1.5.0; only higher option is a major bump — Minor yields nothing.
        var target = VersionPolicy.SelectTarget(V("1.5.0"), Available("1.5.0", "2.0.0"), UpdateStrategy.Minor);

        Assert.Null(target);
    }

    [Fact]
    public void Ignores_versions_at_or_below_current()
    {
        var target = VersionPolicy.SelectTarget(V("2.0.0"), Available("1.0.0", "2.0.0"), UpdateStrategy.Major);

        Assert.Null(target);
    }
}
