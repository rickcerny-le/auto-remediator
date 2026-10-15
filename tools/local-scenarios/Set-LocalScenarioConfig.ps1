#Requires -Version 7
<#
.SYNOPSIS
Enrolls the local scenario repos, applies the global baseline and each repo's own settings through the running API.

.PARAMETER ApiUrl
Base URL of the api resource (see the Aspire dashboard or `aspire describe`), e.g. http://localhost:5123.

.PARAMETER Only
Enable just these scenario repos and disable the rest (nothing is deleted).

.PARAMETER Root
The folder the generator wrote to. Defaults to <repo root>/.local. Its feed folder is used as the feed.

.NOTES
The global baseline (GlobalSettings) and every repo's Settings come from scenarios.psd1, and {feed} in a feed list is
replaced with the absolute local feed path. Re-running resets each repo to the catalogue, so settings edited in the UI
are overwritten (the global baseline too).

The environment variable LOCAL_SCENARIOS_STARTUP is set by the LocalScenarioSetup wrapper (the AppHost's
local-scenarios-enroll resource). When it is set, the closing "next step" line is the one for a start under the
AppHost instead of the manual one. It is an internal detail; do not set it by hand.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ApiUrl,
    [string[]]$Only,
    [string]$Root = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) '.local')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'
if ([Console]::IsOutputRedirected) { $PSStyle.OutputRendering = 'PlainText' }

$catalogue = Import-PowerShellDataFile -LiteralPath (Join-Path $PSScriptRoot 'scenarios.psd1')
$names = @($catalogue.Repos | ForEach-Object { $_.Name })
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })   # also accepts -Only a,b via pwsh -File
if ($Only) {
    $unknown = @($Only | Where-Object { $names -notcontains $_ })
    if ($unknown.Count -gt 0) { throw "Unknown scenario repo(s): $($unknown -join ', '). Known: $($names -join ', ')" }
}

$feed = Join-Path (Resolve-Path -LiteralPath $Root).Path 'feed'
$base = $ApiUrl.TrimEnd('/')

function Invoke-Api {
    param([string]$Method, [string]$Path, $Body)
    $arguments = @{ Method = $Method; Uri = "$base$Path"; ContentType = 'application/json' }
    if ($null -ne $Body) { $arguments.Body = ConvertTo-Json -InputObject $Body -Depth 5 }
    Invoke-RestMethod @arguments
}

function Expand-Feed {
    param($Values)
    [string[]]@($Values | Where-Object { $_ } | ForEach-Object { ([string]$_).Replace('{feed}', $feed) })
}

function Get-Scalar {
    param($Table, [string]$Key)
    if ($Table -and $Table.ContainsKey($Key)) { $Table[$Key] } else { $null }
}

function Get-List {
    param($Table, [string]$Key, [switch]$ExpandFeed)
    $values = if ($Table -and $Table.ContainsKey($Key)) { @($Table[$Key]) } else { @() }
    if ($ExpandFeed) { Expand-Feed $values } else { [string[]]@($values | Where-Object { $_ }) }
}

function ConvertTo-RepositorySettingsBody {
    # RepositorySettingsDto: missing lists become empty, missing scalars null, inheritGlobal defaults to true.
    param($Settings)
    $inherit = if ($Settings -and $Settings.ContainsKey('InheritGlobal')) { [bool]$Settings.InheritGlobal } else { $true }
    $allowPrerelease = Get-Scalar $Settings 'AllowPrerelease'
    [ordered]@{
        inheritGlobal   = $inherit
        patterns        = [string[]]@(Get-List $Settings 'Patterns')
        excludes        = [string[]]@(Get-List $Settings 'Excludes')
        feeds           = [string[]]@(Get-List $Settings 'Feeds' -ExpandFeed)
        ignore          = [string[]]@(Get-List $Settings 'Ignore')
        strategy        = Get-Scalar $Settings 'Strategy'
        allowPrerelease = if ($null -eq $allowPrerelease) { $null } else { [bool]$allowPrerelease }
        schedule        = Get-Scalar $Settings 'Schedule'
    }
}

function Get-SettingsSummary {
    param($Settings)
    if (-not $Settings -or $Settings.Count -eq 0) { return 'inherits' }
    $parts = [Collections.Generic.List[string]]::new()
    if ($Settings.ContainsKey('InheritGlobal') -and -not [bool]$Settings.InheritGlobal) { $parts.Add('no inherit') }
    foreach ($key in 'Patterns', 'Excludes', 'Feeds', 'Ignore', 'Strategy', 'AllowPrerelease', 'Schedule') {
        if ($Settings.ContainsKey($key)) { $parts.Add("$key=$(@($Settings[$key]) -join ',')") }
    }
    $parts -join '; '
}

$existing = @(Invoke-Api -Method Get -Path '/api/repositories' | ForEach-Object { $_ })
$rows = foreach ($spec in $catalogue.Repos) {
    $enabled = if ($Only) { $Only -contains $spec.Name } elseif ($spec.ContainsKey('Enabled')) { [bool]$spec.Enabled } else { $true }
    $target = if ($spec.ContainsKey('TargetBranch')) { $spec.TargetBranch } else { 'main' }
    $repoSettings = Get-Scalar $spec 'Settings'
    $payload = [ordered]@{
        organization = 'local'
        project      = 'scenarios'
        name         = $spec.Name
        enabled      = $enabled
        targetBranch = $target
        settings     = ConvertTo-RepositorySettingsBody $repoSettings
    }
    $match = $existing | Where-Object { $_.name -eq $spec.Name } | Select-Object -First 1
    if ($match) {
        Invoke-Api -Method Put -Path "/api/repositories/$($match.id)" -Body $payload | Out-Null
        $action = 'updated'
    }
    else {
        Invoke-Api -Method Post -Path '/api/repositories' -Body $payload | Out-Null
        $action = 'created'
    }
    [pscustomobject]@{ Name = $spec.Name; Enabled = $enabled; Target = $target; Settings = (Get-SettingsSummary $repoSettings); Action = $action }
}

$globalSettings = $catalogue.GlobalSettings
$allowPrerelease = Get-Scalar $globalSettings 'AllowPrerelease'
$globalBody = [ordered]@{
    patterns        = [string[]]@(Get-List $globalSettings 'Patterns')
    excludes        = [string[]]@(Get-List $globalSettings 'Excludes')
    feeds           = [string[]]@(Get-List $globalSettings 'Feeds' -ExpandFeed)
    strategy        = $(if (Get-Scalar $globalSettings 'Strategy') { Get-Scalar $globalSettings 'Strategy' } else { 'Minor' })
    ignore          = [string[]]@(Get-List $globalSettings 'Ignore')
    allowPrerelease = [bool]$allowPrerelease
    defaultSchedule = Get-Scalar $globalSettings 'DefaultSchedule'
}
Invoke-Api -Method Put -Path '/api/settings' -Body $globalBody | Out-Null

$rows | Format-Table -AutoSize | Out-String | Write-Host
Write-Host "Global baseline: strategy $($globalBody.strategy), allowPrerelease $($globalBody.allowPrerelease), patterns $($globalBody.patterns -join ', ')"
Write-Host "Feed: $feed"
if ($env:LOCAL_SCENARIOS_STARTUP) {
    Write-Host 'Started by the AppHost: the first scheduler pass after start runs every enabled repo; use Run now for a single repo.'
}
else {
    Write-Host 'Next step: start the scheduler for a first pass, or use Run now on a repo.'
}
