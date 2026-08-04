## 1. Dependencies

- [x] 1.1 Add the `Azure.Identity` package to `AutoRemediator.Infrastructure`

## 2. Dual-mode client construction

- [x] 2.1 Add a credential factory that builds a `DefaultAzureCredential`, setting `ManagedIdentityClientId` from `AZURE_CLIENT_ID` when present
- [x] 2.2 Add value-shape helpers: absolute `http`/`https` URI check (Storage) and connection-string marker check (Service Bus)
- [x] 2.3 In `AddInfrastructure`, construct `TableServiceClient`/`BlobServiceClient` from endpoint + credential when the value is a URI, else from the connection string
- [x] 2.4 In `AddInfrastructure`, construct `ServiceBusClient` from FQDN + credential when the value is not a connection string, else from the connection string

## 3. Tests

- [x] 3.1 Keep the existing connection-string registration test passing
- [x] 3.2 Add a test that `AddInfrastructure` with endpoint values (Storage `https` endpoints, Service Bus FQDN) and `AZURE_CLIENT_ID` resolves all abstractions without a network call

## 4. Verification

- [x] 4.1 Run `dotnet build AutoRemediator.sln` and confirm zero errors
- [x] 4.2 Run `dotnet test AutoRemediator.sln` and confirm all tests pass
