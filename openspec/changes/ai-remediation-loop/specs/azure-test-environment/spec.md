## MODIFIED Requirements

### Requirement: Azure AI Foundry model access
The environment SHALL provision Azure AI Foundry access via an AI Services account with a `gpt-4o-mini` model deployment, and SHALL expose the account endpoint and deployment name so they can be supplied to the remediation job as a connection reference for the chat client, rather than as an `Agents`-section endpoint and deployment pair.

#### Scenario: A gpt-4o-mini deployment is available
- **WHEN** the ai-foundry module is applied
- **THEN** it creates an AI Services account with a `gpt-4o-mini` deployment and outputs the endpoint and deployment name for the app configuration

### Requirement: Resource endpoints wired into the apps
Each Container App and Job SHALL receive the endpoints it needs (Storage table/blob endpoints, Service Bus namespace, and — for the remediation job — the Foundry model connection) as environment variables, so the running app resolves services without hard-coded connection strings.

#### Scenario: Apps receive endpoint configuration
- **WHEN** the apps and jobs are applied
- **THEN** the `api` and `web` apps receive the Storage and Service Bus endpoints, and the `remediation` job additionally receives the Foundry model connection under the connection name the chat client resolves, all as environment variables
