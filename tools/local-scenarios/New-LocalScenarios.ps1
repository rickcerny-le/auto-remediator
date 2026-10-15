#Requires -Version 7
<#
.SYNOPSIS
Builds the local scenario data: the NuGet folder feed (.local/feed), the demo repos (.local/repos)
and the pull request records (.local/prs). Skips the rebuild when the existing output is current.

.DESCRIPTION
A SHA-256 fingerprint of this script, LocalScenarios.psm1, scenarios.psd1, the absolute root and the .NET SDK
version is written to <Root>/scenarios.fingerprint.json after a successful build. On the next run the script
rebuilds only if the fingerprint differs or the output is structurally incomplete (a nupkg or repo missing, a
required branch missing, a NoFolder repo present). Commit ids are not compared, so hand commits to main do not
force a rebuild; a rebuild discards them. The fingerprint file is deleted first on every rebuild, so a killed
run is never treated as current.

An exclusive lock on <Root>/.generator.lock stops two generators running at once (exit code 3).

.PARAMETER Root
Output folder. Defaults to <repo root>/.local. feed, repos and prs under it are deleted and recreated on a rebuild.

.PARAMETER Force
Rebuild even when the output is current.

.PARAMETER Reset
Fast reset only (a few seconds, no rebuild): deletes autoremediator/* branches, prunes worktrees, cleans the working
trees, puts every repo on main and empties <Root>/prs. Exits 1 if the output is missing or stale. Not combinable with -Force.

.PARAMETER ResetIfCurrent
Default mode, plus the fast reset when the output is current. After a rebuild nothing more is done.
#>
[CmdletBinding()]
param(
    [string]$Root = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) '.local'),
    [switch]$Force,
    [switch]$Reset,
    [switch]$ResetIfCurrent
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'
if ([Console]::IsOutputRedirected) { $PSStyle.OutputRendering = 'PlainText' }

function Remove-FolderWithRetry {
    param([Parameter(Mandatory)][string]$Path)
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force }
            return
        }
        catch {
            if ($attempt -eq 5) {
                throw "Could not delete $Path ($($_.Exception.Message)). A process is probably holding files in it; " +
                    'stop the app (aspire stop) or run dotnet build-server shutdown, then run this script again.'
            }
            Start-Sleep -Seconds 1
        }
    }
}

if ($Reset -and $Force) {
    [Console]::Error.WriteLine('-Reset and -Force are mutually exclusive: -Reset only resets the repos, -Force rebuilds everything.')
    exit 1
}
if ($Reset -and $ResetIfCurrent) {
    [Console]::Error.WriteLine('-Reset and -ResetIfCurrent are mutually exclusive.')
    exit 1
}

$started = [Diagnostics.Stopwatch]::StartNew()
Import-Module (Join-Path $PSScriptRoot 'LocalScenarios.psm1') -Force

$catalogue = Get-ScenarioCatalogue -Path (Join-Path $PSScriptRoot 'scenarios.psd1')

New-Item -ItemType Directory -Path $Root -Force | Out-Null
$Root = (Resolve-Path -LiteralPath $Root).Path

$lockPath = Join-Path $Root '.generator.lock'
try {
    $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
}
catch [IO.IOException] {
    [Console]::Error.WriteLine("Another local-scenarios generator is running against $Root (lock $lockPath). Wait for it to finish.")
    exit 3
}

try {
    $fingerprint = Get-ScenarioFingerprint -Root $Root -ScriptsDirectory $PSScriptRoot
    if ($Reset) {
        $state = Test-ScenarioOutputCurrent -Root $Root -Catalogue $catalogue -Fingerprint $fingerprint
        if (-not $state.Current) {
            [Console]::Error.WriteLine("Local scenarios are missing or stale ($($state.Reason)); run New-LocalScenarios.ps1 (or -Force) instead of -Reset.")
            exit 1
        }
        Reset-ScenarioRepos -Root $Root -Catalogue $catalogue
        exit 0
    }
    if (-not $Force) {
        $state = Test-ScenarioOutputCurrent -Root $Root -Catalogue $catalogue -Fingerprint $fingerprint
        if ($state.Current) {
            Write-Host "Local scenarios are up to date (fingerprint $($fingerprint.Fingerprint.Substring(0, 12))); skipping rebuild. Use -Force to rebuild."
            if ($ResetIfCurrent) { Reset-ScenarioRepos -Root $Root -Catalogue $catalogue }
            exit 0
        }
        Write-Host "Rebuilding local scenarios: $($state.Reason)"
    }
    else {
        Write-Host 'Rebuilding local scenarios: -Force was specified'
    }

    # First step: an interrupted rebuild must never look current.
    $fingerprintFile = Join-Path $Root 'scenarios.fingerprint.json'
    if (Test-Path -LiteralPath $fingerprintFile) { Remove-Item -LiteralPath $fingerprintFile -Force }

    foreach ($name in 'feed', 'repos', 'prs') {
        $path = Join-Path $Root $name
        Remove-FolderWithRetry -Path $path
        New-Item -ItemType Directory -Path $path | Out-Null
    }

    New-ScenarioFeed -Root $Root -Catalogue $catalogue
    New-ScenarioRepos -Root $Root -Catalogue $catalogue

    $result = Test-ScenarioFeed -Root $Root -Catalogue $catalogue
    if ($result.Errors.Count -gt 0) {
        $result.Errors | ForEach-Object { Write-Error $_ -ErrorAction Continue }
        Write-Host "Self-check FAILED with $($result.Errors.Count) problem(s)."
        exit 1
    }

    Write-ScenarioFingerprint -Root $Root -Fingerprint $fingerprint
    Write-Host ("Feed ready: {0} packages in {1:N0}s ({2})" -f $result.PackageCount, $started.Elapsed.TotalSeconds, (Join-Path $Root 'feed'))
}
finally {
    $lock.Dispose()
}
