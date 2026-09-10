using AutoRemediator.Domain;

namespace AutoRemediator.Domain.Tests;

public class PackagePatternMatcherTests
{
    private static readonly string[] Orion = ["Contoso.*"];

    [Theory]
    [InlineData("Contoso.Core", true)]
    [InlineData("Contoso.Data.Sql", true)]
    [InlineData("contoso.core", true)] // case-insensitive
    [InlineData("Newtonsoft.Json", false)]
    [InlineData("Orion", false)] // prefix without the dot does not match "Contoso.*"
    public void Wildcard_includes_matching_ids(string packageId, bool expected)
    {
        Assert.Equal(expected, PackagePatternMatcher.IsMatch(packageId, Orion, []));
    }

    [Fact]
    public void Excludes_win_over_includes()
    {
        var excludes = new[] { "Contoso.Legacy.*" };

        Assert.True(PackagePatternMatcher.IsMatch("Contoso.Core", Orion, excludes));
        Assert.False(PackagePatternMatcher.IsMatch("Contoso.Legacy.Api", Orion, excludes));
    }

    [Fact]
    public void No_includes_matches_nothing()
    {
        Assert.False(PackagePatternMatcher.IsMatch("Contoso.Core", [], []));
    }
}
