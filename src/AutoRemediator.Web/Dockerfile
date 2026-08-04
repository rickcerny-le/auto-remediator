# Build from the repository root so shared project references resolve:
#   docker build -f src/AutoRemediator.Web/Dockerfile -t <image> .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY . .
RUN dotnet restore "src/AutoRemediator.Web/AutoRemediator.Web.csproj"
RUN dotnet publish "src/AutoRemediator.Web/AutoRemediator.Web.csproj" \
    -c "$BUILD_CONFIGURATION" -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "AutoRemediator.Web.dll"]
