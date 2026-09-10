# Build from the repository root so shared project references resolve:
#   docker build -f src/AutoRemediator.Worker.Remediation/Dockerfile -t <image> .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY . .
RUN dotnet restore "src/AutoRemediator.Worker.Remediation/AutoRemediator.Worker.Remediation.csproj"
RUN dotnet publish "src/AutoRemediator.Worker.Remediation/AutoRemediator.Worker.Remediation.csproj" \
    -c "$BUILD_CONFIGURATION" -o /app/publish --no-restore /p:UseAppHost=false

# The SDK, not the runtime: this worker shells out to `dotnet restore` and `dotnet build`
# against the target repository to verify a change before opening a pull request.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Keep the SDK quiet and non-interactive inside the job.
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

ENTRYPOINT ["dotnet", "AutoRemediator.Worker.Remediation.dll"]
