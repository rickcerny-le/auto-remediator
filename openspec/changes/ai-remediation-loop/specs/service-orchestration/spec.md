## ADDED Requirements

### Requirement: A model resource that runs locally in development
The AppHost SHALL model a Foundry resource with an account-level model deployment, and SHALL configure it to run locally during development so the remediation loop is exercisable without any cloud dependency. The remediation worker SHALL reference the model deployment and wait for it.

Consuming code SHALL be unchanged between local and deployed operation — only the AppHost's modelling of the resource differs. Foundry projects SHALL NOT be used, as they are unsupported when the resource runs locally.

#### Scenario: The model runs locally with the rest of the system
- **WHEN** a developer starts the AppHost
- **THEN** the local model resource starts alongside the emulator-backed resources and the remediation worker receives its connection reference

#### Scenario: The remediation worker waits for the model
- **WHEN** the AppHost starts the remediation worker
- **THEN** it waits for the model deployment to become healthy before starting

#### Scenario: No cloud dependency is introduced for local operation
- **WHEN** the system runs locally
- **THEN** the only external dependency remains the targeted Azure DevOps repository and its package feed
