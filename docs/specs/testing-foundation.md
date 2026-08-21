# testing-foundation Specification

## Purpose

Defines the test project layout, frameworks, and coverage gate for the AutoRemediator solution: xUnit v3 for unit and integration tests, Playwright functional tests against the Blazor Web UI, and a definition-of-done requirement that every scaffolded project carries at least one test.

## Requirements

### Requirement: Test project layout
The `tests/` directory SHALL contain xUnit v3 test projects covering the API, Domain, Infrastructure, Agents, and Workers, plus a dedicated Playwright functional test project targeting the Web UI. Test project names SHALL mirror the system under test (e.g. `AutoRemediator.Api.Tests`, `AutoRemediator.Domain.Tests`, `AutoRemediator.Infrastructure.Tests`, `AutoRemediator.Agents.Tests`, `AutoRemediator.Workers.Tests`, `AutoRemediator.Web.FunctionalTests`).

#### Scenario: Test projects exist and are in the solution
- **WHEN** the solution is loaded
- **THEN** the unit/integration test projects for Api, Domain, Infrastructure, Agents, and Workers are present under `tests/`, along with the Playwright functional test project, and all are included in `AutoRemediator.sln`

### Requirement: xUnit v3 as the unit and integration framework
All non-functional test projects SHALL target xUnit v3, and integration tests for the API SHALL exercise the host through an in-memory or Aspire-hosted test server rather than mocking the HTTP pipeline.

#### Scenario: Unit test run succeeds
- **WHEN** a developer runs `dotnet test AutoRemediator.sln`
- **THEN** the xUnit v3 test projects are discovered and their placeholder tests pass

#### Scenario: API integration test boots the host
- **WHEN** an API integration test runs
- **THEN** it starts the API host through a test server and issues a real HTTP request to a mapped endpoint

### Requirement: Playwright functional tests target the Web UI
The `AutoRemediator.Web.FunctionalTests` project SHALL use Playwright to drive the running Blazor Web App and SHALL contain at least one smoke test that loads the home page and asserts on rendered content.

#### Scenario: Functional smoke test loads the UI
- **WHEN** the functional test suite runs against the started Web application
- **THEN** Playwright navigates to the home page and asserts that expected content is rendered

### Requirement: Tests are the definition-of-done gate
Every project scaffolded in this change SHALL be accompanied by at least one placeholder or smoke test so that `dotnet test` exercises each testable project, honoring the repository's TDD convention.

#### Scenario: Every testable project has coverage entry point
- **WHEN** the test suite runs
- **THEN** at least one test executes against each of the Api, Domain, Infrastructure, Agents, Workers, and Web targets
