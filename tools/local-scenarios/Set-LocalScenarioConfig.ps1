#Requires -Version 7
<#
.SYNOPSIS
Enrolls the local scenario repos and applies a targeting settings profile through the running API.

.PARAMETER ApiUrl
Base URL of the api resource (see the Aspire dashboard or `aspire describe`), e.g. http://localhost:5123.

.PARAMETER Profile
Settings profile: baseline (Minor), patch (Patch), major (Major) or prerelease (Minor + pre-release).

.PARAMETER Only
Enable just these scenario repos and disable the rest (nothing is deleted).

.PARAMETER Root
The folder the generator wrote to. Defaults to <repo root>/.local. Its feed folder is used as the feed.

.NOTES
The environment variable LOCAL_SCENARIOS_STARTUP is set by the LocalScenarioSetup wrapper (the AppHost's
local-scenarios-enroll resource). When it is set, the closing "next step" line says the scheduler pass follows
automatically instead of telling you to restart the scheduler. It is an internal detail; do not set it by hand.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ApiUrl,
    [ValidateSet('baseline', 'patch', 'major', 'prerelease')][string]$Profile = 'baseline',
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

$profiles = @{
    baseline   = @{ Strategy = 'Minor'; AllowPrerelease = $false }
    patch      = @{ Strategy = 'Patch'; AllowPrerelease = $false }
    major      = @{ Strategy = 'Major'; AllowPrerelease = $false }
    prerelease = @{ Strategy = 'Minor'; AllowPrerelease = $true }
}
$selected = $profiles[$Profile]

function Invoke-Api {
    param([string]$Method, [string]$Path, $Body)
    $arguments = @{ Method = $Method; Uri = "$base$Path"; ContentType = 'application/json' }
    if ($null -ne $Body) { $arguments.Body = ConvertTo-Json -InputObject $Body -Depth 5 }
    Invoke-RestMethod @arguments
}

$existing = @(Invoke-Api -Method Get -Path '/api/repositories' | ForEach-Object { $_ })
$rows = foreach ($spec in $catalogue.Repos) {
    $enabled = if ($Only) { $Only -contains $spec.Name } elseif ($spec.ContainsKey('Enabled')) { [bool]$spec.Enabled } else { $true }
    $target = if ($spec.ContainsKey('TargetBranch')) { $spec.TargetBranch } else { 'main' }
    $payload = [ordered]@{
        organization = 'local'
        project      = 'scenarios'
        name         = $spec.Name
        enabled      = $enabled
        targetBranch = $target
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
    [pscustomobject]@{ Name = $spec.Name; Enabled = $enabled; Target = $target; Action = $action }
}

$settings = [ordered]@{
    patterns        = @('Contoso.*', 'Fabrikam.*', 'Northwind.*', 'Tailspin.*')
    excludes        = @()
    feeds           = @($feed)
    strategy        = $selected.Strategy
    ignore          = @('Tailspin.Legacy.*')
    allowPrerelease = $selected.AllowPrerelease
}
Invoke-Api -Method Put -Path '/api/settings' -Body $settings | Out-Null

$rows | Format-Table -AutoSize | Out-String | Write-Host
Write-Host "Active profile: $Profile (strategy $($selected.Strategy), allowPrerelease $($selected.AllowPrerelease))"
Write-Host "Feed: $feed"
if ($env:LOCAL_SCENARIOS_STARTUP) {
    Write-Host 'Started by the AppHost: when LocalRepos__RunOnStart=true the scheduler pass follows automatically; otherwise start the scheduler resource when ready.'
}
else {
    Write-Host 'Next step: restart the scheduler resource (Aspire dashboard, or aspire resource scheduler restart) to trigger a run for every enabled repo.'
}
