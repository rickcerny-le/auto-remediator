#Requires -Version 7
<#
.SYNOPSIS
Self-test for the generator fingerprint and the output-current check. No Pester, no build; runs in a few seconds.
Exits non-zero if any case fails.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$realScripts = $PSScriptRoot
Import-Module (Join-Path $realScripts 'LocalScenarios.psm1') -Force

$script:failures = 0
function Assert-Case {
    param([string]$Name, [bool]$Condition, [string]$Detail = '')
    if ($Condition) { Write-Host "PASS $Name" }
    else {
        $script:failures++
        Write-Host "FAIL $Name$(if ($Detail) { " ($Detail)" })"
    }
}

$temp = Join-Path ([IO.Path]::GetTempPath()) "fingerprint-test-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    # --- hash cases -------------------------------------------------------------------------
    $scripts = Join-Path $temp 'scripts'
    New-Item -ItemType Directory -Path $scripts | Out-Null
    foreach ($name in 'scenarios.psd1', 'LocalScenarios.psm1', 'New-LocalScenarios.ps1') {
        Copy-Item -LiteralPath (Join-Path $realScripts $name) -Destination $scripts
    }
    $psd1 = Join-Path $scripts 'scenarios.psd1'
    $original = [IO.File]::ReadAllText($psd1)
    $lf = $original.Replace("`r`n", "`n").Replace("`r", "`n")
    $utf8 = [Text.UTF8Encoding]::new($false)
    $root = Join-Path $temp 'root'

    $hash = { param($r = $root) (Get-ScenarioFingerprint -Root $r -ScriptsDirectory $scripts).Fingerprint }

    [IO.File]::WriteAllText($psd1, $lf, $utf8)
    $baseline = & $hash
    Assert-Case 'same inputs give the same hash' ($baseline -eq (& $hash))
    Assert-Case 'hash is 64 hex characters' ($baseline -match '^[0-9a-f]{64}$')

    [IO.File]::WriteAllText($psd1, $lf.Replace("`n", "`r`n"), $utf8)
    Assert-Case 'CRLF gives the same hash as LF' ($baseline -eq (& $hash))

    [IO.File]::WriteAllText($psd1, $lf, $utf8)
    Assert-Case 'LF gives the same hash' ($baseline -eq (& $hash))

    [IO.File]::WriteAllText($psd1, $lf, [Text.UTF8Encoding]::new($true))
    Assert-Case 'a BOM gives the same hash' ($baseline -eq (& $hash))

    [IO.File]::WriteAllText($psd1, 'X' + $lf.Substring(1), $utf8)
    Assert-Case 'changing one character gives a different hash' ($baseline -ne (& $hash))

    [IO.File]::WriteAllText($psd1, $lf, $utf8)
    Assert-Case 'a different root gives a different hash' ($baseline -ne (& $hash (Join-Path $temp 'other-root')))

    if ($IsWindows) {
        $a = & $hash 'C:\x'
        $b = & $hash 'c:\X'
        Assert-Case 'root C:\x equals c:\X on Windows' ($a -eq $b)
    }
    else {
        Write-Host 'SKIP root case-insensitivity (Windows only)'
    }

    # --- output-current cases -----------------------------------------------------------------
    $catalogue = Get-ScenarioCatalogue -Path (Join-Path $realScripts 'scenarios.psd1')
    $fp = Get-ScenarioFingerprint -Root $root -ScriptsDirectory $scripts

    $env:GIT_AUTHOR_NAME = 'Test'; $env:GIT_AUTHOR_EMAIL = 'test@example.invalid'
    $env:GIT_COMMITTER_NAME = 'Test'; $env:GIT_COMMITTER_EMAIL = 'test@example.invalid'

    function New-FakeRoot {
        param([string]$Path)
        $feed = Join-Path $Path 'feed'
        New-Item -ItemType Directory -Path $feed -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $Path 'prs') -Force | Out-Null
        foreach ($package in $catalogue.Packages) {
            [IO.File]::WriteAllText((Join-Path $feed (Get-PackageFileName $package)), '')
        }
        foreach ($spec in $catalogue.Repos | Where-Object { -not (Get-RepoOption $_ 'NoFolder' $false) }) {
            $repo = Join-Path $Path "repos/$($spec.Name)"
            New-Item -ItemType Directory -Path $repo -Force | Out-Null
            & git -C $repo init -q -b main 2>&1 | Out-Null
            & git -C $repo -c commit.gpgsign=false commit -q --allow-empty --no-verify -m init 2>&1 | Out-Null
            if (Get-RepoOption $spec 'AgentFix' $false) { & git -C $repo branch scenario/agent-fix 2>&1 | Out-Null }
        }
        Write-ScenarioFingerprint -Root $Path -Fingerprint $fp
    }

    $fake = Join-Path $temp 'fake'
    function Get-State { Test-ScenarioOutputCurrent -Root $fake -Catalogue $catalogue -Fingerprint $fp }
    $template = Join-Path $temp template
    New-FakeRoot -Path $template
    function Reset-Fake { if (Test-Path $fake) { Remove-Item -LiteralPath $fake -Recurse -Force }; Copy-Item -LiteralPath $template -Destination $fake -Recurse }
    function Assert-NotCurrent {
        param([string]$Name, [string]$ReasonPattern)
        $state = Get-State
        Assert-Case $Name ((-not $state.Current) -and $state.Reason -match $ReasonPattern) "Current=$($state.Current), Reason='$($state.Reason)'"
    }

    Reset-Fake
    $state = Get-State
    Assert-Case 'intact fake root is current' $state.Current $state.Reason

    $fpFile = Join-Path $fake 'scenarios.fingerprint.json'
    Remove-Item -LiteralPath $fpFile
    Assert-NotCurrent 'missing fingerprint file is not current' 'fingerprint.*missing'

    Reset-Fake
    [IO.File]::WriteAllText($fpFile, '{ not json')
    Assert-NotCurrent 'corrupt fingerprint file is not current' 'corrupt'

    Reset-Fake
    [IO.File]::WriteAllText($fpFile, '{"fingerprint":"deadbeef"}')
    Assert-NotCurrent 'mismatched fingerprint is not current' 'mismatch'

    Reset-Fake
    $firstPackage = Get-PackageFileName $catalogue.Packages[0]
    Remove-Item -LiteralPath (Join-Path $fake "feed/$firstPackage")
    Assert-NotCurrent 'missing nupkg is not current' ([regex]::Escape($firstPackage))

    Reset-Fake
    [IO.File]::WriteAllText((Join-Path $fake 'feed/Extra.Package.1.0.0.nupkg'), '')
    Assert-NotCurrent 'extra nupkg is not current' 'Extra\.Package'

    Reset-Fake
    $victim = $catalogue.Repos | Where-Object { -not (Get-RepoOption $_ 'NoFolder' $false) } | Select-Object -First 1
    Remove-Item -LiteralPath (Join-Path $fake "repos/$($victim.Name)") -Recurse -Force
    Assert-NotCurrent 'missing repo folder is not current' ([regex]::Escape($victim.Name))

    Reset-Fake
    $agent = $catalogue.Repos | Where-Object { Get-RepoOption $_ 'AgentFix' $false } | Select-Object -First 1
    & git -C (Join-Path $fake "repos/$($agent.Name)") branch -D scenario/agent-fix 2>&1 | Out-Null
    Assert-NotCurrent 'missing scenario/agent-fix is not current' 'agent-fix'

    Reset-Fake
    $noFolder = $catalogue.Repos | Where-Object { Get-RepoOption $_ 'NoFolder' $false } | Select-Object -First 1
    if ($noFolder) {
        New-Item -ItemType Directory -Path (Join-Path $fake "repos/$($noFolder.Name)") | Out-Null
        Assert-NotCurrent 'a NoFolder repo that exists is not current' ([regex]::Escape($noFolder.Name))
    }
    else { Assert-Case 'catalogue has a NoFolder repo' $false 'none found' }

    Reset-Fake
    Remove-Item -LiteralPath (Join-Path $fake 'prs') -Recurse -Force
    Assert-NotCurrent 'missing prs folder is not current' 'prs'

    Reset-Fake
    & git -C (Join-Path $fake "repos/$($victim.Name)") -c commit.gpgsign=false commit -q --allow-empty --no-verify -m 'hand commit' 2>&1 | Out-Null
    $state = Get-State
    Assert-Case 'a hand commit to main stays current' $state.Current $state.Reason

    # --- reset cases ---------------------------------------------------------------------------
    function Get-Snapshot {
        $files = Get-ChildItem -LiteralPath (Join-Path $fake 'feed') -File | Sort-Object Name |
            ForEach-Object { "$($_.Name)=$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)" }
        $fpHash = (Get-FileHash -LiteralPath (Join-Path $fake 'scenarios.fingerprint.json') -Algorithm SHA256).Hash
        ($files -join ';') + "|$fpHash"
    }
    function Get-Ref {
        param([string]$Repo, [string]$Ref)
        (& git -C (Join-Path $fake "repos/$Repo") rev-parse --verify --quiet "refs/heads/$Ref" 2>$null)
    }
    function Get-Branches {
        param([string]$Repo)
        @(& git -C (Join-Path $fake "repos/$Repo") for-each-ref --format='%(refname:short)' refs/heads/)
    }
    function Invoke-FakeGit {
        param([string]$Repo, [string[]]$Arguments)
        & git -C (Join-Path $fake "repos/$Repo") -c commit.gpgsign=false @Arguments 2>&1 | Out-Null
    }

    Reset-Fake
    $target = $victim.Name
    $agentName = $agent.Name
    $other = $catalogue.Repos | Where-Object { -not (Get-RepoOption $_ 'NoFolder' $false) -and $_.Name -ne $target } | Select-Object -First 1
    $targetPath = Join-Path $fake "repos/$target"

    # a hand commit on main must survive; so must a user branch and scenario/agent-fix
    Invoke-FakeGit $target @('commit', '-q', '--allow-empty', '--no-verify', '-m', 'hand commit')
    Invoke-FakeGit $target @('branch', 'my-branch')
    $mainBefore = Get-Ref $target 'main'
    $myBefore = Get-Ref $target 'my-branch'
    $agentBefore = Get-Ref $agentName 'scenario/agent-fix'
    $otherMainBefore = Get-Ref $other.Name 'main'
    $snapshotBefore = Get-Snapshot

    # an update branch with a commit, a second sibling branch, a stray file, a PR record, and a worktree registration
    Invoke-FakeGit $target @('checkout', '-q', '-b', 'autoremediator/dependency-updates')
    Invoke-FakeGit $target @('commit', '-q', '--allow-empty', '--no-verify', '-m', 'update')
    Invoke-FakeGit $target @('branch', 'autoremediator/other')
    [IO.File]::WriteAllText((Join-Path $targetPath 'stray.txt'), 'x')
    [IO.File]::WriteAllText((Join-Path $targetPath 'tracked-edit.txt'), 'x')
    New-Item -ItemType Directory -Path (Join-Path $fake 'prs/x') | Out-Null
    [IO.File]::WriteAllText((Join-Path $fake 'prs/x/1.json'), '{}')
    $wt = Join-Path $temp 'gone-worktree'
    Invoke-FakeGit $target @('worktree', 'add', '--detach', $wt)
    Remove-Item -LiteralPath $wt -Recurse -Force

    $outside = Join-Path $temp 'outside.txt'
    [IO.File]::WriteAllText($outside, 'keep')

    Reset-ScenarioRepos -Root $fake -Catalogue $catalogue | Out-Null

    Assert-Case 'reset deletes autoremediator/dependency-updates' (-not (Get-Ref $target 'autoremediator/dependency-updates'))
    Assert-Case 'reset deletes every autoremediator/* branch' (@(Get-Branches $target | Where-Object { $_ -like 'autoremediator/*' }).Count -eq 0)
    Assert-Case 'reset removes the stray untracked file' (-not (Test-Path -LiteralPath (Join-Path $targetPath 'stray.txt')))
    Assert-Case 'reset empties prs but keeps the folder' ((Test-Path -LiteralPath (Join-Path $fake 'prs') -PathType Container) -and
        @(Get-ChildItem -LiteralPath (Join-Path $fake 'prs') -Force).Count -eq 0)
    Assert-Case 'reset leaves the repo on main' ((& git -C $targetPath symbolic-ref --short HEAD) -eq 'main')
    Assert-Case 'reset leaves a clean working tree' (@(& git -C $targetPath status --porcelain --ignored).Count -eq 0)
    Assert-Case 'reset prunes the dead worktree registration' (@(& git -C $targetPath worktree list --porcelain | Where-Object { $_ -like 'worktree *' }).Count -eq 1)
    Assert-Case 'a hand commit on main survives the reset' ((Get-Ref $target 'main') -eq $mainBefore)
    Assert-Case 'a user branch survives the reset' ((Get-Ref $target 'my-branch') -eq $myBefore -and $myBefore)
    Assert-Case 'scenario/agent-fix survives the reset' ((Get-Ref $agentName 'scenario/agent-fix') -eq $agentBefore -and $agentBefore)
    Assert-Case 'main in another repo is unchanged' ((Get-Ref $other.Name 'main') -eq $otherMainBefore)
    Assert-Case 'feed files and fingerprint file are unchanged (SHA-256)' ((Get-Snapshot) -eq $snapshotBefore)
    Assert-Case 'files outside the root are untouched' ((Test-Path -LiteralPath $outside) -and ([IO.File]::ReadAllText($outside) -eq 'keep'))
    $state = Get-State
    Assert-Case 'output is current after a reset' $state.Current $state.Reason

    Reset-ScenarioRepos -Root $fake -Catalogue $catalogue | Out-Null
    Assert-Case 'a second reset is a no-op that still succeeds' ((Get-Ref $target 'main') -eq $mainBefore)
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

if ($script:failures -gt 0) {
    Write-Host "$script:failures case(s) FAILED"
    exit 1
}
Write-Host 'All cases passed'
