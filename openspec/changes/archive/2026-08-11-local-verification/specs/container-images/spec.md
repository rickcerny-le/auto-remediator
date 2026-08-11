## MODIFIED Requirements

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
