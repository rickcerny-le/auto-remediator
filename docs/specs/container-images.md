# container-images Specification

## Purpose

Defines how each deployable AutoRemediator service is packaged as a container image: a per-service Dockerfile, repo-root build context for shared references, multi-stage builds on .NET 10 base images, and consistent image naming and tagging.

## Requirements

### Requirement: A Dockerfile per deployable service
Each deployable service — `AutoRemediator.Api`, `AutoRemediator.Web`, `AutoRemediator.Worker.Scheduler`, and `AutoRemediator.Worker.Remediation` — SHALL have its own Dockerfile that produces a runnable container image. Library and test projects SHALL NOT have Dockerfiles.

#### Scenario: Every service has a Dockerfile
- **WHEN** the repository is inspected
- **THEN** each of the four deployable service projects contains a Dockerfile, and no library or test project does

### Requirement: Repo-root build context
Each Dockerfile SHALL be built from the repository root so that shared project references (`Shared`, `Domain`, `Contracts`, `Infrastructure`, `Agents`, `ServiceDefaults`) resolve during restore/publish. Each Dockerfile SHALL reference its own service project for restore and publish.

#### Scenario: Image builds with shared references
- **WHEN** a service image is built with the repository root as the build context
- **THEN** the build restores and publishes that service project together with its referenced projects, and completes without missing-reference errors

### Requirement: Multi-stage build on .NET 10 base images
Each Dockerfile SHALL use a multi-stage build: a .NET 10 SDK stage to restore and publish in Release, and a minimal .NET 10 runtime stage for the final image, except where the service itself requires the SDK at run time. Apps (`api`, `web`) SHALL use the ASP.NET Core runtime base and expose the app port; the `scheduler` worker SHALL use the .NET runtime base and expose no port. The `remediation` worker SHALL use the .NET 10 SDK base for its final stage and expose no port, because it invokes `dotnet restore` and `dotnet build` against target repositories at run time.

#### Scenario: App image is a runnable web image
- **WHEN** the `api` or `web` image is built
- **THEN** the final stage is built on the ASP.NET Core 10 runtime, exposes the application port, and its entrypoint runs the service assembly

#### Scenario: Scheduler image is a runnable console image
- **WHEN** the `scheduler` image is built
- **THEN** the final stage is built on the .NET 10 runtime (no web server/port) and its entrypoint runs the worker assembly

#### Scenario: Remediation image can restore and build
- **WHEN** the `remediation` image is built
- **THEN** the final stage is built on the .NET 10 SDK (no web server/port), the `dotnet` SDK commands are available inside it, and its entrypoint runs the worker assembly

### Requirement: Consistent image naming and tags
Images SHALL be named `<acr-login-server>/autoremediator/<service>:<tag>` where `<service>` is one of `api`, `web`, `scheduler`, `remediation`, and each pushed image SHALL be tagged with both the triggering commit's short SHA and `latest`.

#### Scenario: Pushed image carries commit and latest tags
- **WHEN** an image is pushed from a trunk build
- **THEN** it is published under `<acr>/autoremediator/<service>` with the commit short-SHA tag and the `latest` tag
