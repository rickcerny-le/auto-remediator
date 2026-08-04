## RENAMED Requirements

- FROM: `### Requirement: Authenticated read-only Azure DevOps client`
- TO: `### Requirement: Authenticated Azure DevOps client`

## MODIFIED Requirements

### Requirement: Authenticated Azure DevOps client
The system SHALL provide an Azure DevOps client, behind an abstraction, that authenticates to a configured organization and performs both read operations — verifying a repository exists and reading file contents/manifests — and the write operations required for remediation: pushing a commit of changed files to a branch (creating the branch from the target branch head when absent), and creating or finding a pull request. Writes are performed via the Azure DevOps REST API without cloning the repository.

#### Scenario: Read a manifest file from a repository
- **WHEN** the client is asked for the contents of `Directory.Packages.props` in a configured repository
- **THEN** it returns the file's text from the target branch, or a not-found result if the file is absent

#### Scenario: Verify a configured repository exists
- **WHEN** a configured repository is checked against Azure DevOps
- **THEN** the client reports whether that org/project/repository is reachable

#### Scenario: Push a commit to a branch
- **WHEN** the client is asked to push edited manifests to `autoremediator/dependency-updates`
- **THEN** it creates the branch from the target branch head if it does not exist and commits the changed files, or updates the branch if it already exists — without cloning the repository

#### Scenario: Create or find a pull request
- **WHEN** the client is asked to ensure a pull request from the branch into the target branch
- **THEN** it returns the existing active pull request if one exists, otherwise it creates one and returns it
