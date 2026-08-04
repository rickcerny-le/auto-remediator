# Build from the repository root so shared project references resolve:
#   docker build -f src/AutoRemediator.Worker.Remediation/Dockerfile -t <image> .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY . .
RUN dotnet restore "src/AutoRemediator.Worker.Remediation/AutoRemediator.Worker.Remediation.csproj"
RUN dotnet publish "src/AutoRemediator.Worker.Remediation/AutoRemediator.Worker.Remediation.csproj" \
    -c "$BUILD_CONFIGURATION" -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AutoRemediator.Worker.Remediation.dll"]
