> Implemented ahead of this change being written (commits `041d5bb`, `20931b7`); the tasks below
> record what was done so the specs have a change to be synced from.

## 1. Register the Azure clients through the Aspire integrations

- [x] 1.1 Replace the hand-written `TableServiceClient`/`BlobServiceClient`/`ServiceBusClient` factories with `AddAzureTableServiceClient`, `AddAzureBlobServiceClient` and `AddAzureServiceBusClient`
- [x] 1.2 Delete the now-dead dual-mode helpers (`IsHttpEndpoint`, `IsConnectionString`, `RequireConnectionString`) and their unused imports
- [x] 1.3 Set `HealthCheckQueueName` on the Service Bus integration so its health check probes a real queue

## 2. Credential handling

- [x] 2.1 Confirm how `AZURE_CLIENT_ID` reaches deployed services (Terraform merges `identity_env` into every container app's `env_vars`, so it is a process environment variable)
- [x] 2.2 Stop passing an explicit credential to the integrations and delete `CreateCredential`, so a connection-string value keeps the connection-string code path
- [x] 2.3 Correct the stale test comment that implied the identity is pinned from a configuration value

## 3. AppHost wiring

- [x] 3.1 Give the scheduler the `tables` and `blobs` references it needs to read enrolled repositories
- [x] 3.2 Add `WaitFor(storage)` to the scheduler and remediation workers
- [x] 3.3 Start the storage emulator with `--skipApiVersionCheck` so blob container creation works locally

## 4. Tests

- [x] 4.1 Assert `TableServiceClient`, `BlobServiceClient` and `ServiceBusClient` resolve from the container
- [x] 4.2 Assert each integration registered a health check for its resource
- [x] 4.3 Assert a missing connection value fails with the connection name in the message

## 5. Verification

- [x] 5.1 Run the full test suite (143 passed, 0 failed)
- [x] 5.2 Start the app with `aspire start` and confirm every resource reports Healthy via `aspire describe`
- [x] 5.3 Confirm the API's `/health` returns 200 with no unhealthy check in its logs, and that `/alive` still returns 200
- [x] 5.4 Confirm the scheduler reaches Table Storage and completes its run
