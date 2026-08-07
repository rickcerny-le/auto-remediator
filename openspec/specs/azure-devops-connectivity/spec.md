# azure-devops-connectivity Specification

## Purpose

Defines the Azure DevOps client behind an abstraction: authenticating to a configured organization with a PAT sourced from Azure Key Vault (with local user-secrets fallback), verifying repositories, discovering dependency manifests, reading file contents, retrieving the full tree as an archive for local verification, and performing the write operations required for remediation — pushing commits to a branch and creating or finding pull requests via the REST API without cloning.

## Requirements

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

### Requirement: PAT sourced from Key Vault with local fallback
The Azure DevOps credential (a PAT) SHALL be read from configuration, sourced from Azure Key Vault via managed identity when running in Azure and from user-secrets (or equivalent local configuration) during local development. The PAT SHALL NOT be committed to source or stored in Terraform state.

#### Scenario: Credential resolves from Key Vault in Azure
- **WHEN** the app runs with a Key Vault URI configured
- **THEN** the PAT is read from Key Vault through the managed identity and used to authenticate to Azure DevOps

#### Scenario: Credential resolves locally without Key Vault
- **WHEN** the app runs locally with the PAT supplied via user-secrets
- **THEN** the client authenticates using that value without requiring Key Vault

### Requirement: Manifest discovery
For a repository, the client SHALL be able to locate the dependency manifests relevant to analysis — `Directory.Packages.props` and `*.csproj` files — so their contents can be read.

#### Scenario: Locate manifests in a repository
- **WHEN** manifests are requested for a configured repository
- **THEN** the client returns the paths/contents of the `Directory.Packages.props` and `*.csproj` files present on the default branch

### Requirement: Repository tree download as an archive
The Azure DevOps client SHALL expose retrieval of a repository's full tree at a specified commit as a zip archive, using the same authenticated REST connection as manifest discovery. This SHALL NOT require a `git` binary, a clone, or any credential beyond the PAT already used for reads. Repositories using Git LFS or submodules are not supported by this retrieval.

Unlike the read paths, which return empty or null on failure, this operation SHALL surface a failed request to the caller — silently returning an empty tree would let a run verify nothing and report success.

#### Scenario: The tree is retrieved at a commit
- **WHEN** the client is asked for a repository's tree at a specific commit id
- **THEN** it returns a zip archive of the repository content at that commit, obtained over the REST API without cloning

#### Scenario: Download failure is reported, not swallowed
- **WHEN** the archive request fails or returns a non-success status
- **THEN** the client surfaces the failure to the caller so verification can be classified as skipped
