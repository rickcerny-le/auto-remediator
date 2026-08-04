# AutoRemediator — CI/CD (Azure DevOps)

Trunk-based pipeline that validates every pull request and, on merge to trunk
(`main`), builds and pushes a container image per service and rolls them out to
the ACA **test** environment.

## Flow

```
PR into main   ─▶ Validate: dotnet build + test
                            build all 4 images (no push)
                            terraform fmt/validate
                   (branch policy blocks the PR on failure)

merge to main  ─▶ Validate ─▶ Package: build + push 4 images (:<shortSha>, :latest)
                           ─▶ Deploy:  az containerapp[/job] update ×4 to :<shortSha>
```

Push and deploy stages are gated by `ne(Build.Reason, 'PullRequest')`, so PR runs
never push or deploy.

## Files

```
azure-pipelines.yml                     # entry pipeline (trigger main + pr main)
.azuredevops/templates/
  build-test.yml                        # restore, build, dotnet test
  docker-build.yml                      # build one service image, optional push
  deploy-aca.yml                        # az containerapp[/job] update ×4
  terraform-validate.yml                # fmt -check + init -backend=false + validate
src/<service>/Dockerfile                # one per deployable service
```

## Images

`<acrLoginServer>/autoremediator/<service>:<tag>` where `<service>` ∈
`api | web | scheduler | remediation`. Trunk pushes two tags: the 8-char commit
SHA (immutable — what Deploy references) and `latest`.

## One-time setup

This pipeline assumes the repo lives in **Azure Repos** with trunk = `main`.
Before the first run:

1. **Service connection** — create an Azure Resource Manager service connection
   (workload identity federation recommended). Grant its identity, on the test
   resource group: **AcrPush** on the registry and **Container Apps Contributor**
   (or a role allowing `containerapp update`). Note its name.

2. **Variable group** — create a variable group named **`autoremediator-test`**
   (Pipelines → Library) with:

   | Variable | Example | Source |
   | --- | --- | --- |
   | `azureServiceConnection` | `sc-autoremediator` | the service connection name |
   | `acrLoginServer` | `autremtestacr<sfx>.azurecr.io` | `terraform output acr_login_server` |
   | `resourceGroup` | `rg-autrem-test` | `terraform output resource_group_name` |
   | `apiApp` | `ca-autrem-test-api` | ACA app name |
   | `webApp` | `ca-autrem-test-web` | ACA app name |
   | `schedulerJob` | `caj-autrem-test-scheduler` | ACA job name |
   | `remediationJob` | `caj-autrem-test-remediation` | ACA job name |

3. **Create the pipeline** — point Pipelines at `azure-pipelines.yml`.

4. **Branch policy** — on `main`, require pull requests and add the pipeline as a
   **build validation** policy so the Validate stage must pass before merge.
   This is what makes the flow trunk-based.

## Notes

- **Terraform on the agent:** `terraform-validate.yml` uses the `terraform`
  pre-installed on `ubuntu-latest`. To pin a version, add the `TerraformInstaller`
  task (Terraform marketplace extension) ahead of the validate steps.
- **Deploy vs Terraform drift:** Deploy updates the ACA image directly, so the
  running image no longer matches the Terraform `*_image` variables/state. Treat
  the running image as pipeline-owned. When you next run Terraform, set the
  `*_image` variables to the same `:<shortSha>` tag to reconcile (or the apply
  will try to reset the image to the placeholder/last-known value).
- **First deploy** replaces the placeholder image the Terraform provisioned with
  a real service image.
- **Rollback:** re-run Deploy against a previous commit SHA, or
  `az containerapp update --image <acr>/autoremediator/<svc>:<oldsha>`.
