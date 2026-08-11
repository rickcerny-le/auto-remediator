## MODIFIED Requirements

### Requirement: Microsoft Agent Framework isolated in the Agents library
The `AutoRemediator.Agents` project SHALL house the Microsoft Agent Framework (MAF) agents, tools, and orchestration wiring and SHALL expose a single DI registration extension. The agent SHALL be constructed over a chat client injected from a named connection reference, not from an endpoint and deployment name read from its own configuration section. Registration SHALL make no network call.

#### Scenario: Agents registers MAF wiring via one extension
- **WHEN** a host calls the Agents DI registration extension
- **THEN** the MAF agent abstraction(s) are registered in the service container over the injected chat client, with no network call made at registration time
