## ADDED Requirements

### Requirement: A model resource that runs locally in development
The AppHost SHALL model a Foundry resource with an account-level model deployment, and SHALL configure it to run locally during development so the remediation loop is exercisable without any cloud dependency. Modelling the resource SHALL NOT require any Azure subscription or location to be configured for the system to start locally. Foundry projects SHALL NOT be used, as they are unsupported when the resource runs locally.

Consuming code SHALL be unchanged between local and deployed operation — only the AppHost's modelling of the resource differs.

#### Scenario: The model runs locally with the rest of the system
- **WHEN** a developer starts the AppHost
- **THEN** the local model resource starts alongside the emulator-backed resources and the remediation worker receives its connection reference

#### Scenario: Starting locally needs no Azure configuration
- **WHEN** the AppHost starts with no Azure subscription or location configured
- **THEN** it starts successfully and reports no provisioning error

#### Scenario: No cloud dependency is introduced for local operation
- **WHEN** the system runs locally
- **THEN** the only external dependency remains the targeted Azure DevOps repository and its package feed

### Requirement: The model is a soft dependency of the remediation worker
The remediation worker SHALL reference the model deployment but SHALL NOT wait for it to become healthy before starting. An unavailable or unhealthy model SHALL cost the system its repair capability only — mechanical bumps SHALL continue to be computed, verified and delivered as pull requests.

Gating the worker on the model would stop dependency updates entirely because a repair capability was missing, which inverts the degradation principle the rest of the pipeline follows.

#### Scenario: An unavailable model does not stop the pipeline
- **WHEN** the model resource fails to start and a run's bump verifies cleanly
- **THEN** the remediation worker still runs and the run opens its pull request

#### Scenario: An unavailable model costs only repairs
- **WHEN** the model is unavailable and a run's bump is rejected with compile diagnostics
- **THEN** the run ends in its verification-failed terminal status recording that remediation was unavailable, exactly as it would with no agent configured

#### Scenario: The worker starts without the model
- **WHEN** the AppHost starts and the model resource is not healthy
- **THEN** the remediation worker reaches a running state rather than waiting indefinitely
