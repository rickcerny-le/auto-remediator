## 1. Dockerfiles (one per service)

- [x] 1.1 Add `src/AutoRemediator.Api/Dockerfile` — multi-stage, SDK build (repo-root context, restore+publish the Api project), ASP.NET Core 10 runtime final stage, expose 8080, entrypoint `AutoRemediator.Api.dll`
- [x] 1.2 Add `src/AutoRemediator.Web/Dockerfile` — same pattern for the Blazor Web app (ASP.NET Core runtime, expose 8080, entrypoint `AutoRemediator.Web.dll`)
- [x] 1.3 Add `src/AutoRemediator.Worker.Scheduler/Dockerfile` — .NET 10 runtime final stage (no port), entrypoint `AutoRemediator.Worker.Scheduler.dll`
- [x] 1.4 Add `src/AutoRemediator.Worker.Remediation/Dockerfile` — .NET 10 runtime final stage (no port), entrypoint `AutoRemediator.Worker.Remediation.dll`
- [x] 1.5 Add a `.dockerignore` at the repo root excluding `bin/`, `obj/`, `.git/`, test artifacts, and `infra/` to keep build context small

## 2. Pipeline step templates

- [x] 2.1 Add `.azuredevops/templates/build-test.yml` — restore, build (Release), `dotnet test` with results/coverage published
- [x] 2.2 Add `.azuredevops/templates/docker-build.yml` — parameters `service`, `dockerfile`, `push` (bool), image name/tags; `az acr login` + `docker build`, and `docker push` only when `push` is true
- [x] 2.3 Add `.azuredevops/templates/deploy-aca.yml` — `AzureCLI@2` steps that `az containerapp update` (api, web) and `az containerapp job update` (scheduler, remediation) to the commit-tagged images
- [x] 2.4 Add `.azuredevops/templates/terraform-validate.yml` — install Terraform, `fmt -check`, `init -backend=false`, `validate` in `infra/terraform/environments/test`

## 3. Top-level pipeline

- [x] 3.1 Add `azure-pipelines.yml` with `trigger: [main]` and `pr: [main]`, and a variables block referencing the `autoremediator-test` variable group (acrLoginServer, resourceGroup, azureServiceConnection, resource names) + computed short-SHA tag
- [x] 3.2 `Validate` stage (always): build-test template, terraform-validate template, and docker-build for all four services with `push: false`
- [x] 3.3 `Package` stage (condition: not a PR): docker-build for all four services with `push: true`, tagging short-SHA + `latest`
- [x] 3.4 `Deploy` stage (condition: not a PR, depends on Package): deploy-aca template updating the four resources to the short-SHA images
- [x] 3.5 Define the service→project/Dockerfile matrix once and reuse it across the Validate/Package stages

## 4. Documentation

- [x] 4.1 Add `.azuredevops/README.md` — trunk-based flow, one-time setup (Azure Repos with trunk `main`, ARM service connection with AcrPush + Container Apps Contributor, the `autoremediator-test` variable group and its values), the PR branch policy that requires the validation build, image tagging, and the deploy-vs-Terraform image reconciliation note

## 5. Verification

- [x] 5.1 Build each service image locally (from repo root, via Podman) and confirm all four build successfully *(api 249MB, web 282MB, scheduler 222MB, remediation 226MB — all OK)*
- [x] 5.2 Lint the pipeline YAML structure (valid YAML, templates resolve) *(structural self-review — full `az pipelines validate` needs a live Azure DevOps connection)*
- [ ] 5.3 Confirm `terraform fmt -check` + `init -backend=false` + `validate` succeed for `infra/terraform/environments/test` *(not run — Terraform execution restricted this session; run locally / it is exactly what the PR build runs)*
