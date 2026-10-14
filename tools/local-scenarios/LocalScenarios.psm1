#Requires -Version 7
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Functions behind New-LocalScenarios.ps1. Scenario data lives in scenarios.psd1.

function Invoke-Dotnet {
    param([Parameter(Mandatory)][string[]]$Arguments, [string]$WorkingDirectory)
    $previous = Get-Location
    try {
        if ($WorkingDirectory) { Set-Location -LiteralPath $WorkingDirectory }
        $output = & dotnet @Arguments 2>&1
        if ($LASTEXITCODE -ne 0) {
            $output | ForEach-Object { Write-Host $_ }
            throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
        }
    }
    finally { Set-Location -LiteralPath $previous.Path }
}

function Get-ScenarioCatalogue {
    param([Parameter(Mandatory)][string]$Path)
    Import-PowerShellDataFile -LiteralPath $Path
}

function Get-PackageFileName {
    param($Package)
    "$($Package.Id).$($Package.Version).nupkg"
}

function Get-GlobalPackagesFolder {
    if ($env:NUGET_PACKAGES) { return $env:NUGET_PACKAGES }
    $line = (& dotnet nuget locals global-packages --list) | Select-Object -First 1
    if ($LASTEXITCODE -ne 0 -or $line -notmatch 'global-packages:\s*(.+)$') {
        throw 'Could not determine the NuGet global-packages folder.'
    }
    $Matches[1].Trim()
}

function Clear-PackageCache {
    # Restore reuses <global-packages>/<id>/<version> even when the feed's nupkg changed, so stale
    # entries for generated ids would surface later as NU1403 or a wrong API.
    param([Parameter(Mandatory)]$Catalogue)
    $folder = Get-GlobalPackagesFolder
    $ids = @($Catalogue.Packages | ForEach-Object { $_.Id }) + @($Catalogue.ObsoleteIds) |
        ForEach-Object { $_.ToLowerInvariant() } | Sort-Object -Unique
    foreach ($id in $ids) {
        $path = Join-Path $folder $id
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    Write-Host "Purged $($ids.Count) package ids from $folder"
}

function Get-ScenarioSource {
    param([string]$Id, [string]$Version, [string]$Api)
    $ns = $Id
    $versionConst = "    public const string Version = `"$Version`";"
    switch ($Api) {
        'Info' {
@"
namespace $ns
{
    public static class PackageInfo
    {
$versionConst
        public static string Describe() => "$Id " + Version;
    }
}
"@
        }
        'Serializer1' {
@"
namespace $ns
{
    public static class Serializer
    {
$versionConst
        public static string Serialize(object value) => value?.ToString() ?? "null";
    }
}
"@
        }
        'Serializer2' {
@"
namespace $ns
{
    public static class Serializer
    {
$versionConst
        public static string Serialize(object value) => value?.ToString() ?? "null";
        public static string SerializeIndented(object value) => "  " + Serialize(value);
    }
}
"@
        }
        'Messaging3' {
@"
namespace $ns
{
    public sealed class MessageBus
    {
$versionConst
        public void Publish(string topic, string payload) { }
    }
}
"@
        }
        'Messaging4' {
@"
using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

namespace $ns
{
    public sealed record Message(string Topic, string Payload);

    public sealed class MessageBus
    {
$versionConst
        public Task PublishAsync(Message message) => Task.CompletedTask;
    }
}
"@
        }
        default { throw "Unknown Api variant '$Api' for $Id $Version." }
    }
}

function Write-ScenarioProject {
    param([string]$Directory, $Package, [string]$NuspecPath)
    $refs = ''
    if (-not ($Package.ContainsKey('Nuspec') -and $Package.Nuspec) -and $Package.ContainsKey('Dependencies')) {
        foreach ($d in $Package.Dependencies) {
            $refs += "    <PackageReference Include=`"$($d.Id)`" Version=`"$($d.Range)`" />`n"
        }
    }
    $nuspecProps = ''
    if ($Package.ContainsKey('Nuspec') -and $Package.Nuspec) {
        $nuspecProps = "    <NuspecFile>$NuspecPath</NuspecFile>`n    <NuspecBasePath>$Directory</NuspecBasePath>`n"
    }
    $csproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <AssemblyName>$($Package.Id)</AssemblyName>
    <RootNamespace>$($Package.Id)</RootNamespace>
    <Authors>local-scenarios</Authors>
    <Description>Generated local scenario package $($Package.Id).</Description>
$nuspecProps  </PropertyGroup>
  <ItemGroup>
$refs  </ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $Directory "$($Package.Id).csproj") -Value $csproj
    Set-Content -LiteralPath (Join-Path $Directory 'Source.cs') -Value (Get-ScenarioSource -Id $Package.Id -Version $Package.Version -Api $Package.Api)
}

function Write-ScenarioNuspec {
    param([string]$Path, $Package)
    $deps = ($Package.Dependencies | ForEach-Object {
            "        <dependency id=`"$($_.Id)`" version=`"$($_.Range)`" />"
        }) -join "`n"
    $nuspec = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>$($Package.Id)</id>
    <version>$($Package.Version)</version>
    <authors>local-scenarios</authors>
    <description>Generated local scenario package $($Package.Id).</description>
    <dependencies>
      <group targetFramework=".NETStandard2.0">
$deps
      </group>
    </dependencies>
  </metadata>
  <files>
    <file src="bin/Release/netstandard2.0/$($Package.Id).dll" target="lib/netstandard2.0" />
  </files>
</package>
"@
    Set-Content -LiteralPath $Path -Value $nuspec
}

function New-ScenarioFeed {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)]$Catalogue
    )
    $feed = Join-Path $Root 'feed'
    New-Item -ItemType Directory -Path $feed -Force | Out-Null
    $feed = (Resolve-Path -LiteralPath $feed).Path

    Clear-PackageCache -Catalogue $Catalogue

    $work = Join-Path ([IO.Path]::GetTempPath()) "local-scenarios-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        # Only the output feed, so dependency ranges restore from what was already packed.
        $config = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-scenarios" value="$feed" />
  </packageSources>
</configuration>
"@
        Set-Content -LiteralPath (Join-Path $work 'NuGet.config') -Value $config

        foreach ($package in $Catalogue.Packages) {
            $dir = Join-Path $work $package.Id
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
            $nuspecPath = Join-Path $dir "$($package.Id).nuspec"
            if ($package.ContainsKey('Nuspec') -and $package.Nuspec) { Write-ScenarioNuspec -Path $nuspecPath -Package $package }
            Write-ScenarioProject -Directory $dir -Package $package -NuspecPath $nuspecPath

            Write-Host "Packing $($package.Id) $($package.Version)"
            Invoke-Dotnet -WorkingDirectory $dir -Arguments @(
                'pack', "$($package.Id).csproj", '-c', 'Release',
                "-p:PackageVersion=$($package.Version)",
                '-p:NoWarn=NU5104%3BNU5128%3BCS1591',
                '-o', $feed, '--nologo', '-v', 'q')
        }
    }
    finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
    # Packing restored some generated ids into the global cache; drop them so nothing predates the next restore.
    Clear-PackageCache -Catalogue $Catalogue
}

function Get-NupkgDependencies {
    param([string]$Path)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -like '*.nuspec' -and $_.FullName -notlike '*/*' } | Select-Object -First 1
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $xml = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }
    $result = @{}
    foreach ($d in $xml.SelectNodes("//*[local-name()='dependency']")) {
        $result[$d.GetAttribute('id')] = $d.GetAttribute('version')
    }
    $result
}

function Test-MessagingHasNoPublish {
    # Loads the 4.0.0 assembly from its nupkg (in memory, no temp file) and reflects over it.
    param([string]$NupkgPath)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($NupkgPath)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq 'lib/netstandard2.0/Northwind.Messaging.dll' }
        if (-not $entry) { throw 'Northwind.Messaging dll missing from nupkg.' }
        $memory = [IO.MemoryStream]::new()
        $stream = $entry.Open()
        try { $stream.CopyTo($memory) } finally { $stream.Dispose() }
    }
    finally { $zip.Dispose() }
    $assembly = [Reflection.Assembly]::Load($memory.ToArray())
    $bus = $assembly.GetType('Northwind.Messaging.MessageBus', $true)
    $names = $bus.GetMethods() | ForEach-Object { $_.Name }
    ($names -notcontains 'Publish') -and ($names -contains 'PublishAsync')
}

function Test-ScenarioFeed {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)]$Catalogue)
    $feed = Join-Path $Root 'feed'
    $errors = [Collections.Generic.List[string]]::new()

    $expected = @($Catalogue.Packages | ForEach-Object { Get-PackageFileName $_ }) | Sort-Object
    $actual = @(Get-ChildItem -LiteralPath $feed -File | ForEach-Object Name) | Sort-Object
    foreach ($m in $expected | Where-Object { $actual -notcontains $_ }) { $errors.Add("missing from feed: $m") }
    foreach ($x in $actual | Where-Object { $expected -notcontains $_ }) { $errors.Add("unexpected in feed: $x") }

    $normalize = { param($range) ($range -replace '\s', '') }
    foreach ($package in $Catalogue.Packages) {
        $file = Join-Path $feed (Get-PackageFileName $package)
        if (-not (Test-Path -LiteralPath $file)) { continue }
        $declared = Get-NupkgDependencies -Path $file
        $wanted = @{}
        if ($package.ContainsKey('Dependencies')) {
            foreach ($d in $package.Dependencies) { $wanted[$d.Id] = $d.Range }
        }
        foreach ($id in $wanted.Keys) {
            if (-not $declared.ContainsKey($id)) { $errors.Add("$($package.Id) $($package.Version): dependency $id not declared"); continue }
            if ((& $normalize $declared[$id]) -ne (& $normalize $wanted[$id])) {
                $errors.Add("$($package.Id) $($package.Version): $id range is '$($declared[$id])', expected '$($wanted[$id])'")
            }
        }
        foreach ($id in $declared.Keys | Where-Object { -not $wanted.ContainsKey($_) }) {
            $errors.Add("$($package.Id) $($package.Version): unexpected dependency $id")
        }
    }

    $messaging4 = Join-Path $feed 'Northwind.Messaging.4.0.0.nupkg'
    if (Test-Path -LiteralPath $messaging4) {
        if (-not (Test-MessagingHasNoPublish -NupkgPath $messaging4)) {
            $errors.Add('Northwind.Messaging 4.0.0 must have PublishAsync and no Publish method')
        }
    }

    [pscustomobject]@{ PackageCount = $actual.Count; Errors = $errors.ToArray() }
}

function Get-RepoOption {
    param($Repo, [string]$Key, $Default = $null)
    if ($Repo.ContainsKey($Key)) { $Repo[$Key] } else { $Default }
}

function Write-RepoFile {
    # UTF-8 without a BOM, LF line endings.
    param([string]$Repo, [string]$RelativePath, [string]$Content)
    $path = Join-Path $Repo $RelativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    $text = ($Content -replace "`r`n", "`n").TrimEnd("`n") + "`n"
    [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
}

function Invoke-Git {
    param([Parameter(Mandatory)][string]$Repo, [Parameter(Mandatory)][string[]]$Arguments)
    $output = & git -C $Repo -c commit.gpgsign=false @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | ForEach-Object { Write-Host $_ }
        throw "git $($Arguments -join ' ') failed in $Repo (exit code $LASTEXITCODE)"
    }
    $output
}

function Get-ConsumerStatement {
    # A statement that really uses the package's API, so a breaking change breaks the build.
    param($Catalogue, [string]$Id, [string]$Version)
    $package = $Catalogue.Packages | Where-Object { $_.Id -eq $Id -and $_.Version -eq $Version } | Select-Object -First 1
    if (-not $package) { throw "Package $Id $Version is not in the catalogue." }
    switch ($package.Api) {
        'Info' { "System.Console.WriteLine($Id.PackageInfo.Describe());" }
        { $_ -in 'Serializer1', 'Serializer2' } { "System.Console.WriteLine($Id.Serializer.Serialize(`"payload`"));" }
        'Messaging3' { "new $Id.MessageBus().Publish(`"topic`", `"payload`");" }
        'Messaging4' { "new $Id.MessageBus().PublishAsync(new $Id.Message(`"topic`", `"payload`")).GetAwaiter().GetResult();" }
        default { throw "No consumer statement for Api '$($package.Api)'." }
    }
}

function Write-ProgramFile {
    param([string]$Repo, [string]$Project, $Catalogue, $Packages)
    $lines = @($Packages | ForEach-Object { Get-ConsumerStatement -Catalogue $Catalogue -Id $_.Id -Version $_.Version })
    Write-RepoFile -Repo $Repo -RelativePath "$Project/Program.cs" -Content ($lines -join "`n")
}

function Get-ScenarioRepoProjects {
    param($Spec)
    @(Get-RepoOption $Spec 'Projects' @('src/App'))
}

function Write-ScenarioRepoFiles {
    param([string]$Repo, [string]$Feed, $Spec, $Catalogue)
    $cpm = $Spec.Layout -eq 'Cpm'
    $lockFile = [bool](Get-RepoOption $Spec 'LockFile' $false)
    $projects = Get-ScenarioRepoProjects $Spec

    $outcomes = $Spec.Outcomes
    $readme = @"
# $($Spec.Name)

$($Spec.Summary)

Generated by tools/local-scenarios/New-LocalScenarios.ps1. Do not edit by hand; regenerate instead.

## Expected outcome per settings profile

| Profile | Outcome |
| --- | --- |
| baseline (Minor) | $($outcomes.baseline) |
| patch | $($outcomes.patch) |
| major | $($outcomes.major) |
| prerelease (Minor + pre-release) | $($outcomes.prerelease) |
"@
    Write-RepoFile $Repo 'README.md' $readme
    Write-RepoFile $Repo '.gitignore' "bin/`nobj/"
    Write-RepoFile $Repo 'NuGet.config' @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-scenarios" value="$Feed" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="local-scenarios">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@

    if ($cpm) {
        $versions = ($Spec.Packages | ForEach-Object { "    <PackageVersion Include=`"$($_.Id)`" Version=`"$($_.Version)`" />" }) -join "`n"
        Write-RepoFile $Repo 'Directory.Packages.props' @"
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
$versions
  </ItemGroup>
</Project>
"@
    }

    foreach ($project in $projects) {
        $name = Split-Path -Leaf $project
        $refs = ($Spec.Packages | ForEach-Object {
                if ($cpm) { "    <PackageReference Include=`"$($_.Id)`" />" }
                else { "    <PackageReference Include=`"$($_.Id)`" Version=`"$($_.Version)`" />" }
            }) -join "`n"
        $lockLine = if ($lockFile) { "`n    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>" } else { '' }
        Write-RepoFile $Repo "$project/$name.csproj" @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>$lockLine
  </PropertyGroup>
  <ItemGroup>
$refs
  </ItemGroup>
</Project>
"@
        Write-ProgramFile -Repo $Repo -Project $project -Catalogue $Catalogue -Packages $Spec.Packages
    }

    if (Get-RepoOption $Spec 'Solution' $false) {
        $entries = ($projects | ForEach-Object { "  <Project Path=`"$_/$(Split-Path -Leaf $_).csproj`" />" }) -join "`n"
        Write-RepoFile $Repo "$($Spec.Name).slnx" "<Solution>`n$entries`n</Solution>"
    }
}

function Get-RepoBuildTargets {
    param([string]$Repo, $Spec)
    if (Get-RepoOption $Spec 'Solution' $false) { return , "$($Spec.Name).slnx" }
    @(Get-ScenarioRepoProjects $Spec | ForEach-Object { "$_/$(Split-Path -Leaf $_).csproj" })
}

function Invoke-RepoBuildCheck {
    # Builds, then removes bin/ and obj/ so no build output is left in (or committed to) the repo.
    param([string]$Repo, $Spec)
    foreach ($target in Get-RepoBuildTargets -Repo $Repo -Spec $Spec) {
        Invoke-Dotnet -WorkingDirectory $Repo -Arguments @('build', $target, '--nologo', '-v', 'q')
    }
    Invoke-Git -Repo $Repo -Arguments @('clean', '-xdf') | Out-Null
}

function New-ScenarioRepos {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)]$Catalogue)
    $reposRoot = Join-Path $Root 'repos'
    New-Item -ItemType Directory -Path $reposRoot -Force | Out-Null
    $feed = Join-Path (Resolve-Path -LiteralPath $Root).Path 'feed'
    $date = '2026-01-01T00:00:00+00:00'
    $agentDate = '2026-01-02T00:00:00+00:00'

    $saved = @{}
    $identity = @{
        GIT_AUTHOR_NAME = 'Local Scenarios'; GIT_AUTHOR_EMAIL = 'local-scenarios@example.invalid'
        GIT_COMMITTER_NAME = 'Local Scenarios'; GIT_COMMITTER_EMAIL = 'local-scenarios@example.invalid'
        GIT_AUTHOR_DATE = $date; GIT_COMMITTER_DATE = $date
    }
    foreach ($key in $identity.Keys) { $saved[$key] = [Environment]::GetEnvironmentVariable($key) }
    try {
        foreach ($key in $identity.Keys) { Set-Item -Path "env:$key" -Value $identity[$key] }

        foreach ($spec in $Catalogue.Repos | Where-Object { -not (Get-RepoOption $_ 'NoFolder' $false) }) {
            $repo = Join-Path $reposRoot $spec.Name
            Write-Host "Creating repo $($spec.Name)"
            New-Item -ItemType Directory -Path $repo | Out-Null
            $repo = (Resolve-Path -LiteralPath $repo).Path
            Write-ScenarioRepoFiles -Repo $repo -Feed $feed -Spec $spec -Catalogue $Catalogue

            Invoke-Git $repo @('init', '-q', '-b', 'main') | Out-Null
            Invoke-Git $repo @('config', 'core.autocrlf', 'false') | Out-Null

            if (Get-RepoOption $spec 'LockFile' $false) {
                # Restore now so packages.lock.json is part of the commit.
                foreach ($target in Get-RepoBuildTargets -Repo $repo -Spec $spec) {
                    Invoke-Dotnet -WorkingDirectory $repo -Arguments @('restore', $target, '--nologo', '-v', 'q')
                }
            }

            Invoke-Git $repo @('add', '-A') | Out-Null
            Invoke-Git $repo @('commit', '-q', '--no-verify', '-m', "Scenario repo: $($spec.Name)") | Out-Null
            Invoke-RepoBuildCheck -Repo $repo -Spec $spec

            if (Get-RepoOption $spec 'AgentFix' $false) {
                $fix = $spec.AgentFix
                $old = $spec.Packages | Where-Object { $_.Id -eq $fix.Package } | Select-Object -First 1
                $env:GIT_AUTHOR_DATE = $agentDate
                $env:GIT_COMMITTER_DATE = $agentDate
                Invoke-Git $repo @('checkout', '-q', '-b', 'scenario/agent-fix') | Out-Null
                $propsPath = Join-Path $repo 'Directory.Packages.props'
                $props = [IO.File]::ReadAllText($propsPath).Replace("Include=`"$($fix.Package)`" Version=`"$($old.Version)`"", "Include=`"$($fix.Package)`" Version=`"$($fix.Version)`"")
                [IO.File]::WriteAllText($propsPath, $props, [Text.UTF8Encoding]::new($false))
                $newPackages = $spec.Packages | ForEach-Object {
                    if ($_.Id -eq $fix.Package) { @{ Id = $_.Id; Version = $fix.Version } } else { $_ }
                }
                foreach ($project in Get-ScenarioRepoProjects $spec) {
                    Write-ProgramFile -Repo $repo -Project $project -Catalogue $Catalogue -Packages $newPackages
                }
                Invoke-RepoBuildCheck -Repo $repo -Spec $spec
                Invoke-Git $repo @('add', '-A') | Out-Null
                Invoke-Git $repo @('commit', '-q', '--no-verify', '-m', "Agent fix: bump $($fix.Package) to $($fix.Version)") | Out-Null
                Invoke-Git $repo @('checkout', '-q', 'main') | Out-Null
                $env:GIT_AUTHOR_DATE = $date
                $env:GIT_COMMITTER_DATE = $date
            }
        }
    }
    finally {
        foreach ($key in $identity.Keys) {
            if ($null -eq $saved[$key]) { Remove-Item -Path "env:$key" -ErrorAction SilentlyContinue }
            else { Set-Item -Path "env:$key" -Value $saved[$key] }
        }
    }

    $result = Test-ScenarioRepos -Root $Root -Catalogue $Catalogue
    if ($result.Errors.Count -gt 0) {
        $result.Errors | ForEach-Object { Write-Error $_ -ErrorAction Continue }
        throw "Repo self-check failed with $($result.Errors.Count) problem(s)."
    }
    Write-Host "Repos ready: $($result.RepoCount) in $reposRoot"
}

function Test-ScenarioRepos {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)]$Catalogue)
    $reposRoot = Join-Path $Root 'repos'
    $feed = Join-Path (Resolve-Path -LiteralPath $Root).Path 'feed'
    $errors = [Collections.Generic.List[string]]::new()
    $count = 0
    foreach ($spec in $Catalogue.Repos) {
        $repo = Join-Path $reposRoot $spec.Name
        if (Get-RepoOption $spec 'NoFolder' $false) {
            if (Test-Path -LiteralPath $repo) { $errors.Add("$($spec.Name): folder must not exist") }
            continue
        }
        if (-not (Test-Path -LiteralPath $repo)) { $errors.Add("$($spec.Name): folder missing"); continue }
        $count++
        $branch = (Invoke-Git $repo @('rev-parse', '--abbrev-ref', 'HEAD') | Select-Object -First 1)
        if ($branch -ne 'main') { $errors.Add("$($spec.Name): HEAD is '$branch', expected main") }
        $commits = (Invoke-Git $repo @('rev-list', '--count', 'main') | Select-Object -First 1)
        if ($commits -ne '1') { $errors.Add("$($spec.Name): main has $commits commits, expected 1") }
        if (Invoke-Git $repo @('status', '--porcelain')) { $errors.Add("$($spec.Name): working tree is not clean") }
        $config = [IO.File]::ReadAllText((Join-Path $repo 'NuGet.config'))
        if (-not $config.Contains("value=`"$feed`"")) { $errors.Add("$($spec.Name): NuGet.config does not contain the absolute feed path $feed") }
        $branches = @(Invoke-Git $repo @('branch', '--format=%(refname:short)')) | Sort-Object
        $wanted = @('main') + $(if (Get-RepoOption $spec 'AgentFix' $false) { 'scenario/agent-fix' } else { @() }) | Sort-Object
        if (($branches -join ',') -ne ($wanted -join ',')) { $errors.Add("$($spec.Name): branches are '$($branches -join ',')', expected '$($wanted -join ',')'") }
        if (Get-RepoOption $spec 'LockFile' $false) {
            $lock = & git -C $repo show 'main:src/App/packages.lock.json' 2>$null
            if ($LASTEXITCODE -ne 0) { $errors.Add("$($spec.Name): packages.lock.json is not committed") }
            else {
                $json = ($lock -join "`n") | ConvertFrom-Json
                $resolved = $json.dependencies.'net10.0'.'Contoso.Logging'.resolved
                if ($resolved -ne '1.2.0') { $errors.Add("$($spec.Name): lock file pins Contoso.Logging '$resolved', expected 1.2.0") }
            }
        }
    }
    [pscustomobject]@{ RepoCount = $count; Errors = $errors.ToArray() }
}

Export-ModuleMember -Function New-ScenarioFeed, New-ScenarioRepos, Test-ScenarioFeed, Test-ScenarioRepos, Get-ScenarioCatalogue
