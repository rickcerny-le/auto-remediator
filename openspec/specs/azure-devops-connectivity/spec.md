# azure-devops-connectivity Specification

## Purpose

Defines the read-only Azure DevOps client behind an abstraction: authenticating to a configured organization with a PAT sourced from Azure Key Vault (with local user-secrets fallback), verifying repositories, discovering dependency manifests, and reading file contents — with no write operations in scope.

## Requirements

### Requirement: Authenticated read-only Azure DevOps client
The system SHALL provide an Azure DevOps client, behind an abstraction, that authenticates to a configured organization and performs read-only operations: verifying a repository exists and reading file contents from a repository's default branch. It SHALL NOT perform any write operation (no branch, commit, or pull request) in this capability.

#### Scenario: Read a manifest file from a repository
- **WHEN** the client is asked for the contents of `Directory.Packages.props` in a configured repository
- **THEN** it returns the file's text from the default branch, or a not-found result if the file is absent

#### Scenario: Verify a configured repository exists
- **WHEN** a configured repository is checked against Azure DevOps
- **THEN** the client reports whether that org/project/repository is reachable

#### Scenario: No write operations are exposed
- **WHEN** the connectivity abstraction is inspected
- **THEN** it exposes only read operations; creating branches/commits/PRs is out of scope here

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
