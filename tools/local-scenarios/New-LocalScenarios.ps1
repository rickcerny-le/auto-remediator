#Requires -Version 7
<#
.SYNOPSIS
Rebuilds the local scenario data: the NuGet folder feed (.local/feed), the demo repos (.local/repos)
and the pull request records (.local/prs).

.PARAMETER Root
Output folder. Defaults to <repo root>/.local. feed, repos and prs under it are deleted and recreated.
#>
[CmdletBinding()]
param(
    [string]$Root = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) '.local')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$started = [Diagnostics.Stopwatch]::StartNew()
Import-Module (Join-Path $PSScriptRoot 'LocalScenarios.psm1') -Force

$catalogue = Get-ScenarioCatalogue -Path (Join-Path $PSScriptRoot 'scenarios.psd1')

New-Item -ItemType Directory -Path $Root -Force | Out-Null
$Root = (Resolve-Path -LiteralPath $Root).Path

foreach ($name in 'feed', 'repos', 'prs') {
    $path = Join-Path $Root $name
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
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
Write-Host ("Feed ready: {0} packages in {1:N0}s ({2})" -f $result.PackageCount, $started.Elapsed.TotalSeconds, (Join-Path $Root 'feed'))
