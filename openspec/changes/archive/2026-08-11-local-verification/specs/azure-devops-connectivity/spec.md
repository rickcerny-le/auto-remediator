## ADDED Requirements

### Requirement: Repository tree download as an archive
The Azure DevOps client SHALL expose retrieval of a repository's full tree at a specified commit as a zip archive, using the same authenticated REST connection as manifest discovery. This SHALL NOT require a `git` binary, a clone, or any credential beyond the PAT already used for reads. Repositories using Git LFS or submodules are not supported by this retrieval.

#### Scenario: The tree is retrieved at a commit
- **WHEN** the client is asked for a repository's tree at a specific commit id
- **THEN** it returns a zip archive of the repository content at that commit, obtained over the REST API without cloning

#### Scenario: Download failure is reported, not swallowed
- **WHEN** the archive request fails or returns a non-success status
- **THEN** the client surfaces the failure to the caller so verification can be classified as skipped
