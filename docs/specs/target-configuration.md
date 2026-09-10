# target-configuration Specification

## Purpose

Defines how AutoRemediator persists and manages what it acts on: an explicit list of managed Azure DevOps repositories and global package-targeting settings (patterns, excludes, feeds, and update policy), stored durably in Azure Table Storage and editable through the API and the Blazor configuration UI.

## Requirements

### Requirement: Managed repository configuration
The system SHALL persist an explicit list of managed repositories, each identified by Azure DevOps organization, project, and repository name, with an `enabled` flag and a target branch. Configured repositories SHALL be the only repositories the tool acts on.

#### Scenario: Add and retrieve a managed repository
- **WHEN** a repository (org/project/name, target branch) is created via the API
- **THEN** it is persisted and returned by subsequent list/get calls with a stable id

#### Scenario: Disabled repositories are excluded from processing
- **WHEN** a managed repository is marked `enabled = false`
- **THEN** it is retained in configuration but excluded from analysis and run enqueue

### Requirement: Global targeting settings
The system SHALL persist global targeting settings: one or more package **patterns** (package-ID globs such as `Contoso.*`), optional **exclude** patterns, one or more package **feeds** used to resolve latest versions, and an update **policy** (at minimum an update strategy and an ignore list). Settings SHALL be editable via the API.

#### Scenario: Configure a package pattern
- **WHEN** the pattern `Contoso.*` is added to targeting settings
- **THEN** it is persisted and used by analysis to select matching packages

#### Scenario: Excludes narrow the matched set
- **WHEN** an exclude pattern is configured alongside an include pattern
- **THEN** packages matching an exclude are not treated as matched even if they match an include

### Requirement: Persistence in Azure Table Storage
Configuration SHALL be stored in Azure Table Storage via the infrastructure storage abstraction, using the managed-identity/endpoint access model in Azure and the emulator locally.

#### Scenario: Configuration round-trips through storage
- **WHEN** configuration is written and the service is restarted
- **THEN** the previously written repositories and targeting settings are read back unchanged

### Requirement: Configuration management UI
The Blazor web app SHALL provide a page to view and edit the managed repositories and global targeting settings (patterns, excludes, feeds, policy).

#### Scenario: Manage configuration from the UI
- **WHEN** an operator opens the configuration page
- **THEN** they can view current repositories and targeting settings and submit changes that persist via the API
