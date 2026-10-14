@{
    # Package catalogue for the local NuGet folder feed (.local/feed).
    # Entries are packed in the order listed, so a package must come after anything it depends on.
    #
    #   Id / Version : package identity
    #   Api          : source variant (see LocalScenarios.psm1, Get-ScenarioSource)
    #                    Info          stable marker type, same API on every version
    #                    Messaging3    MessageBus.Publish(string, string)
    #                    Messaging4    Publish removed; PublishAsync(Message) + Message record
    #                    Serializer1   Serializer.Serialize(object)
    #                    Serializer2   Serializer1 plus SerializeIndented (additive)
    #   Dependencies : PackageReference ranges, written verbatim into the nupkg
    #   Nuspec       : $true = packed from a hand-written nuspec (the dependency is not restored)
    #
    # Every package also exposes `Version` (a const) so a consumer can show what it compiled against.
    Packages = @(
        @{ Id = 'Contoso.Logging'; Version = '1.2.0'; Api = 'Info' }
        @{ Id = 'Contoso.Logging'; Version = '1.2.1'; Api = 'Info' }
        @{ Id = 'Contoso.Logging'; Version = '1.2.3'; Api = 'Info' }

        @{ Id = 'Contoso.Http'; Version = '2.1.0'; Api = 'Info' }
        @{ Id = 'Contoso.Http'; Version = '2.1.3'; Api = 'Info' }
        @{ Id = 'Contoso.Http'; Version = '2.2.0'; Api = 'Info' }
        @{ Id = 'Contoso.Http'; Version = '2.4.1'; Api = 'Info' }
        @{ Id = 'Contoso.Http'; Version = '3.0.0'; Api = 'Info' }

        @{ Id = 'Northwind.Serialization'; Version = '1.4.0'; Api = 'Serializer1' }
        @{ Id = 'Northwind.Serialization'; Version = '2.0.0'; Api = 'Serializer2' }

        @{ Id = 'Northwind.Messaging'; Version = '3.2.0'; Api = 'Messaging3' }
        @{ Id = 'Northwind.Messaging'; Version = '4.0.0'; Api = 'Messaging4' }

        @{ Id = 'Tailspin.Core'; Version = '1.0.0'; Api = 'Info' }
        @{ Id = 'Tailspin.Core'; Version = '1.0.2'; Api = 'Info' }

        @{ Id = 'Tailspin.Legacy.Data'; Version = '1.0.0'; Api = 'Info' }
        @{ Id = 'Tailspin.Legacy.Data'; Version = '1.1.0'; Api = 'Info' }

        @{ Id = 'Fabrikam.Telemetry'; Version = '1.0.0'; Api = 'Info' }
        @{ Id = 'Fabrikam.Telemetry'; Version = '1.0.1'; Api = 'Info' }
        @{ Id = 'Fabrikam.Telemetry'; Version = '1.1.0-beta.2'; Api = 'Info' }

        @{ Id = 'Fabrikam.Data.Abstractions'; Version = '1.0.0'; Api = 'Info' }
        @{ Id = 'Fabrikam.Data.Abstractions'; Version = '2.0.0'; Api = 'Info' }

        @{ Id = 'Fabrikam.Data'; Version = '1.0.0'; Api = 'Info'
           Dependencies = @(@{ Id = 'Fabrikam.Data.Abstractions'; Range = '[1.0.0,2.0.0)' }) }
        @{ Id = 'Fabrikam.Data'; Version = '1.2.0'; Api = 'Info'
           Dependencies = @(@{ Id = 'Fabrikam.Data.Abstractions'; Range = '[2.0.0,3.0.0)' }) }

        @{ Id = 'Fabrikam.Data.SqlServer'; Version = '1.0.0'; Api = 'Info'
           Dependencies = @(@{ Id = 'Fabrikam.Data'; Range = '[1.0.0,2.0.0)' }) }
        @{ Id = 'Fabrikam.Data.SqlServer'; Version = '1.2.0'; Api = 'Info'
           Dependencies = @(@{ Id = 'Fabrikam.Data'; Range = '[1.2.0,2.0.0)' }) }

        @{ Id = 'Fabrikam.Reporting'; Version = '1.0.0'; Api = 'Info'
           Dependencies = @(@{ Id = 'Fabrikam.Data.Abstractions'; Range = '[1.0.0,2.0.0)' }) }
        # Deliberately unresolvable: Fabrikam.Data.Abstractions 3.x never exists on the feed.
        @{ Id = 'Fabrikam.Reporting'; Version = '1.1.0'; Api = 'Info'; Nuspec = $true
           Dependencies = @(@{ Id = 'Fabrikam.Data.Abstractions'; Range = '[3.0.0,4.0.0)' }) }

        @{ Id = 'AdventureWorks.Text'; Version = '1.0.0'; Api = 'Info' }
        @{ Id = 'AdventureWorks.Text'; Version = '1.5.0'; Api = 'Info' }
    )

    # Ids that are purged from the global-packages cache although the catalogue does not produce them
    # (left over from earlier spikes).
    ObsoleteIds = @('Contoso.Core')

    # Demo repos (.local/repos/<Name>). Enrolled as local/scenarios/<Name>.
    #
    #   Layout       : Cpm (Directory.Packages.props) | PerProject (Version= on each PackageReference)
    #   Packages     : declared packages (Id + Version); every project references all of them
    #   Projects     : project folders (default src/App); each becomes an Exe on net10.0
    #   Solution     : write a root .slnx listing the projects
    #   LockFile     : RestorePackagesWithLockFile, restored before the commit so packages.lock.json is committed
    #   AgentFix     : adds branch scenario/agent-fix that bumps Package to Version and fixes the consumer call
    #   NoFolder     : enrolled but never created on disk
    #   Enabled / TargetBranch : used by Set-LocalScenarioConfig.ps1 (defaults true / main)
    #   Outcomes     : expected result per settings profile, written into the repo README
    Repos = @(
        @{ Name = 'patch-bump'; Layout = 'Cpm'
           Summary = 'Central package management, one patch-only package that is two patches behind.'
           Packages = @(@{ Id = 'Contoso.Logging'; Version = '1.2.0' })
           Outcomes = @{ baseline = 'Contoso.Logging -> 1.2.3, Completed'; patch = 'same'; major = 'same'; prerelease = 'same' } }

        @{ Name = 'minor-bump'; Layout = 'PerProject'; Solution = $true
           Summary = 'Two projects (src/Api, src/Worker) and a solution, both declaring Contoso.Http 2.1.0 with Version= on the reference.'
           Projects = @('src/Api', 'src/Worker')
           Packages = @(@{ Id = 'Contoso.Http'; Version = '2.1.0' })
           Outcomes = @{ baseline = 'Contoso.Http -> 2.4.1, both csproj edited, Completed'; patch = '-> 2.1.3'; major = '-> 3.0.0'; prerelease = '-> 2.4.1' } }

        @{ Name = 'major-compiles'; Layout = 'Cpm'
           Summary = 'A major bump (Northwind.Serialization 1.4.0 -> 2.0.0) whose API change is additive, so it compiles.'
           Packages = @(@{ Id = 'Northwind.Serialization'; Version = '1.4.0' })
           Outcomes = @{ baseline = 'NoUpdates (policy cap)'; patch = 'NoUpdates'; major = '-> 2.0.0, Completed'; prerelease = 'NoUpdates' } }

        @{ Name = 'major-breaking'; Layout = 'Cpm'
           Summary = 'A major bump (Northwind.Messaging 3.2.0 -> 4.0.0) that removes MessageBus.Publish(string, string), so the consumer fails with CS1061.'
           Packages = @(@{ Id = 'Northwind.Messaging'; Version = '3.2.0' })
           Outcomes = @{ baseline = 'NoUpdates'; patch = 'NoUpdates'; major = 'VerificationFailed, CS1061, 0 attempts, "No model is configured" transcript, no branch'; prerelease = 'NoUpdates' } }

        @{ Name = 'up-to-date'; Layout = 'PerProject'
           Summary = 'Already on the newest Contoso.Logging.'
           Packages = @(@{ Id = 'Contoso.Logging'; Version = '1.2.3' })
           Outcomes = @{ baseline = 'NoUpdates; the dependency map shows UpToDate'; patch = 'same'; major = 'same'; prerelease = 'same' } }

        @{ Name = 'no-matches'; Layout = 'PerProject'
           Summary = 'Only a package that no targeting pattern matches.'
           Packages = @(@{ Id = 'AdventureWorks.Text'; Version = '1.0.0' })
           Outcomes = @{ baseline = 'NoUpdates; nothing matched'; patch = 'same'; major = 'same'; prerelease = 'same' } }

        @{ Name = 'ignored-package'; Layout = 'Cpm'
           Summary = 'Tailspin.Core is updatable; Tailspin.Legacy.Data matches the ignore glob Tailspin.Legacy.*.'
           Packages = @(@{ Id = 'Tailspin.Core'; Version = '1.0.0' }, @{ Id = 'Tailspin.Legacy.Data'; Version = '1.0.0' })
           Outcomes = @{ baseline = 'Tailspin.Core -> 1.0.2 only; Tailspin.Legacy.Data shows Ignored'; patch = 'same'; major = 'same'; prerelease = 'same' } }

        @{ Name = 'prerelease'; Layout = 'Cpm'
           Summary = 'Fabrikam.Telemetry has a 1.1.0-beta.2 pre-release on the feed.'
           Packages = @(@{ Id = 'Fabrikam.Telemetry'; Version = '1.0.0' })
           Outcomes = @{ baseline = '-> 1.0.1'; patch = '-> 1.0.1'; major = '-> 1.0.1'; prerelease = '-> 1.1.0-beta.2' } }

        @{ Name = 'family-alignment'; Layout = 'Cpm'
           Summary = 'Fabrikam.Data 1.2.0 needs Fabrikam.Data.Abstractions 2.0.0, which forces an escalation beyond policy.'
           Packages = @(@{ Id = 'Fabrikam.Data.Abstractions'; Version = '1.0.0' }, @{ Id = 'Fabrikam.Data'; Version = '1.0.0' }, @{ Id = 'Fabrikam.Data.SqlServer'; Version = '1.0.0' })
           Outcomes = @{ baseline = 'SqlServer and Data -> 1.2.0; Abstractions -> 2.0.0 as collateral beyond policy; Completed'; patch = 'NoUpdates (to confirm)'; major = 'all to the highest (Abstractions matched)'; prerelease = 'as baseline' } }

        @{ Name = 'restore-conflict'; Layout = 'Cpm'
           Summary = 'Fabrikam.Reporting 1.1.0 depends on Fabrikam.Data.Abstractions 3.x, which never exists, so restore fails.'
           Packages = @(@{ Id = 'Fabrikam.Reporting'; Version = '1.0.0' }, @{ Id = 'Fabrikam.Data.Abstractions'; Version = '1.0.0' })
           Outcomes = @{ baseline = 'Reporting -> 1.1.0; VerificationFailed with an NU diagnostic; the AI loop is not entered'; patch = 'NoUpdates'; major = 'as baseline, plus Abstractions -> 2.0.0'; prerelease = 'as baseline' } }

        @{ Name = 'lock-file'; Layout = 'Cpm'; LockFile = $true
           Summary = 'RestorePackagesWithLockFile with a committed packages.lock.json (no locked mode).'
           Packages = @(@{ Id = 'Contoso.Logging'; Version = '1.2.0' })
           Outcomes = @{ baseline = '-> 1.2.3; the pushed commit includes the regenerated lock file'; patch = 'same'; major = 'same'; prerelease = 'same' } }

        @{ Name = 'held-review'; Layout = 'Cpm'
           AgentFix = @{ Package = 'Northwind.Messaging'; Version = '4.0.0' }
           Summary = 'Branch scenario/agent-fix holds the change an AI agent would have proposed (Northwind.Messaging 4.0.0 plus PublishAsync). Seeded as AwaitingReview by task 08.'
           Packages = @(@{ Id = 'Northwind.Messaging'; Version = '3.2.0' })
           Outcomes = @{ baseline = 'Seeded AwaitingReview; the scheduler records SkippedHeld'; patch = 'same'; major = 'same'; prerelease = 'same' } }

        @{ Name = 'missing-branch'; Layout = 'Cpm'; TargetBranch = 'develop'
           Summary = 'Only main exists, but the repo is enrolled with target branch develop.'
           Packages = @(@{ Id = 'Contoso.Logging'; Version = '1.2.0' })
           Outcomes = @{ baseline = 'NoUpdates (mirrors Azure DevOps; known gap)'; patch = 'same'; major = 'same'; prerelease = 'same' } }

        @{ Name = 'does-not-exist'; NoFolder = $true
           Summary = 'Enrolled but there is no folder.'
           Outcomes = @{ baseline = 'NoUpdates (mirrors Azure DevOps; known gap)'; patch = 'same'; major = 'same'; prerelease = 'same' } }

        @{ Name = 'disabled-repo'; Layout = 'Cpm'; Enabled = $false
           Summary = 'Same shape as patch-bump, enrolled with Enabled=false.'
           Packages = @(@{ Id = 'Contoso.Logging'; Version = '1.2.0' })
           Outcomes = @{ baseline = 'No run is created'; patch = 'same'; major = 'same'; prerelease = 'same' } }
    )
}
