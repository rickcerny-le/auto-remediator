using AutoRemediator.Domain;

namespace AutoRemediator.Domain.Tests;

public class PackagePatternMatcherTests
{
    private static readonly string[] Orion = ["Orion180.*"];

    [Theory]
    [InlineData("Orion180.Core", true)]
    [InlineData("Orion180.Data.Sql", true)]
    [InlineData("orion180.core", true)] // case-insensitive
    [InlineData("Newtonsoft.Json", false)]
    [InlineData("Orion", false)] // prefix without the dot does not match "Orion180.*"
    public void Wildcard_includes_matching_ids(string packageId, bool expected)
    {
        Assert.Equal(expected, PackagePatternMatcher.IsMatch(packageId, Orion, []));
    }

    [Fact]
    public void Excludes_win_over_includes()
    {
        var excludes = new[] { "Orion180.Legacy.*" };

        Assert.True(PackagePatternMatcher.IsMatch("Orion180.Core", Orion, excludes));
        Assert.False(PackagePatternMatcher.IsMatch("Orion180.Legacy.Api", Orion, excludes));
    }

    [Fact]
    public void No_includes_matches_nothing()
    {
        Assert.False(PackagePatternMatcher.IsMatch("Orion180.Core", [], []));
    }
}
