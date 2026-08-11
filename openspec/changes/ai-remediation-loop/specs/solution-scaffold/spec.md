## MODIFIED Requirements

### Requirement: Microsoft Agent Framework isolated in the Agents library
The `AutoRemediator.Agents` project SHALL house the Microsoft Agent Framework (MAF) agents, tools, and orchestration wiring and SHALL expose a single DI registration extension. The agent SHALL be constructed over a chat client injected from a named connection reference, not from an endpoint and deployment name read from its own configuration section. Registration SHALL make no network call.

The agent's **contract** — the abstraction the run orchestration calls, the diagnostics it is given, and the edits it proposes — SHALL live in the domain, not in the Agents project. `Agents` and `Infrastructure` are siblings: the run orchestration in `Infrastructure` must call the agent, so a contract owned by `Agents` would require project references in both directions, and referencing `Agents` from `Infrastructure` would pull MAF into `Infrastructure`'s transitive closure. Keeping the contract in the domain is what makes this isolation real rather than nominal.

The contract SHALL NOT expose infrastructure types such as the verification workspace; source access SHALL be a narrow abstraction the domain owns, which infrastructure adapts.

#### Scenario: Agents registers MAF wiring via one extension
- **WHEN** a host calls the Agents DI registration extension
- **THEN** the MAF agent abstraction(s) are registered in the service container over the injected chat client, with no network call made at registration time

#### Scenario: MAF does not leak into infrastructure
- **WHEN** the project references are inspected
- **THEN** `Infrastructure` does not reference `Agents`, and neither references the other; both depend on the domain where the agent contract lives

#### Scenario: The contract is free of infrastructure types
- **WHEN** the agent contract is inspected
- **THEN** it names only domain types — the diagnostics, the proposed edits, and a source-reader abstraction — and no verification workspace or client type
