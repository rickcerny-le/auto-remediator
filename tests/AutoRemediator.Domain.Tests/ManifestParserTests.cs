using AutoRemediator.Domain;

namespace AutoRemediator.Domain.Tests;

public class ManifestParserTests
{
    [Fact]
    public void Parses_central_package_management_versions()
    {
        const string props = """
            <Project>
              <ItemGroup>
                <PackageVersion Include="Contoso.Core" Version="1.2.3" />
                <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """;

        var packages = ManifestParser.Parse(props);

        Assert.Contains(packages, p => p.Id == "Contoso.Core" && p.Version == "1.2.3");
        Assert.Contains(packages, p => p.Id == "Newtonsoft.Json" && p.Version == "13.0.3");
    }

    [Fact]
    public void Parses_packagereference_attribute_and_child_element()
    {
        const string csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Contoso.Data" Version="2.0.0" />
                <PackageReference Include="Serilog">
                  <Version>3.1.1</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """;

        var packages = ManifestParser.Parse(csproj);

        Assert.Contains(packages, p => p.Id == "Contoso.Data" && p.Version == "2.0.0");
        Assert.Contains(packages, p => p.Id == "Serilog" && p.Version == "3.1.1");
    }

    [Fact]
    public void Unresolved_msbuild_variable_version_is_null()
    {
        const string csproj = """
            <Project>
              <ItemGroup>
                <PackageReference Include="Contoso.Core" Version="$(OrionVersion)" />
              </ItemGroup>
            </Project>
            """;

        var packages = ManifestParser.Parse(csproj);

        var pkg = Assert.Single(packages);
        Assert.Equal("Contoso.Core", pkg.Id);
        Assert.Null(pkg.Version);
    }

    [Fact]
    public void Malformed_or_empty_content_yields_no_packages()
    {
        Assert.Empty(ManifestParser.Parse(""));
        Assert.Empty(ManifestParser.Parse("<not-xml"));
    }
}
