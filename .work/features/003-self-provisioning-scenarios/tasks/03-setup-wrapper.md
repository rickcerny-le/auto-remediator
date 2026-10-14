# 03 LocalScenarioSetup wrapper (provision and reset) with tests

**Status:** done
**Feature:** 003-self-provisioning-scenarios
**Depends on:** 02

## What to do
Create the small console tool the AppHost runs as project resources. It exists so that a missing tool produces a readable line in the resource logs instead of an opaque start failure, and so that the API URL can come from Aspire service discovery (task 05). It contains no scenario logic; it only invokes the PowerShell scripts.

1. Create `tools/local-scenarios/LocalScenarioSetup/LocalScenarioSetup.csproj` (`net10.0`, `Exe`, nullable, implicit usings, no project references). Add it to `AutoRemediator.sln` under the existing `tools` solution folder, next to `HeldRunSeeder`.
2. Usage: `LocalScenarioSetup <provision|reset> --scripts <dir> --root <dir> [--no-reset]`. Put the logic in small public or internal static classes so it can be tested:
   - `ToolLocator.Find(string name, string? pathVariable, string? pathExt)` returns the full path or null. It searches PATH entries in order and, on Windows, tries each PATHEXT extension (`pwsh` → `pwsh.exe`).
   - `Preflight.Check(mode, locator)` checks the required tools for the mode: `provision` needs `pwsh`, `git` and `dotnet`; `reset` needs `pwsh` and `git`. It returns the missing tools with a one-line hint each:
     - `pwsh`: "PowerShell 7 (pwsh) is required for local scenarios; Windows PowerShell 5.1 is not supported. Install it from https://aka.ms/powershell, then aspire stop / aspire start."
     - `git`: "git is required for local repository mode."
     - `dotnet`: ".NET 10 SDK is required to build the scenario feed and repos."
   - `ScriptCommand.For(mode, scripts, root, noReset)` builds the `pwsh` argument list:
     - provision: `-NoProfile -NonInteractive -File <scripts>/New-LocalScenarios.ps1 -Root <root> -ResetIfCurrent`, without the last switch when `--no-reset` is passed;
     - reset: `... -Root <root> -Reset`.
3. `Program.cs` parses the arguments (unknown or missing ones exit 1 with usage) and runs the preflight. If tools are missing, it writes `LocalRepos is enabled but <tool> was not found on PATH. <hint>` to stderr for each one and exits 2. Otherwise it starts `pwsh` with `ProcessStartInfo.ArgumentList`, no shell and redirected stdout/stderr. It copies each line to its own stdout/stderr as it arrives, prefixes nothing, and returns pwsh's exit code. It also prints one first line, `local-scenarios: <mode> root=<root>`, and one last line, `local-scenarios: <mode> finished with exit code N in X s`.
4. Add a test project `tests/AutoRemediator.Tools.Tests` (xUnit v3, same packages and conventions as the other test projects) that references `LocalScenarioSetup`. Add it to the solution. Tests:
   - `ToolLocator` finds a fake `pwsh.exe` or `pwsh` in a temp PATH dir, honours PATHEXT on Windows, and returns null when it is absent.
   - `Preflight` reports exactly the missing tools per mode.
   - `ScriptCommand` produces the expected argument lists for provision, provision with `--no-reset`, and reset, including paths with spaces (passed as separate `ArgumentList` items, never re-quoted).
   - Argument parsing rejects unknown modes and missing `--scripts`/`--root`.

## Files
- `tools/local-scenarios/LocalScenarioSetup/LocalScenarioSetup.csproj` (new)
- `tools/local-scenarios/LocalScenarioSetup/Program.cs` and helper classes (new)
- `tests/AutoRemediator.Tools.Tests/` (new project and tests)
- `AutoRemediator.sln`: both projects

## Out of scope
- The `enroll` mode (task 05).
- Registering the resources in the AppHost (task 04).
- Any change to the PowerShell scripts.

## Done when
- [ ] `dotnet build AutoRemediator.sln` and `dotnet test AutoRemediator.sln` pass, and the new tests run (not skipped).
- [ ] `dotnet run --project tools/local-scenarios/LocalScenarioSetup -- provision --scripts <abs>/tools/local-scenarios --root <abs>/.local` behaves like the script with `-ResetIfCurrent` and exits 0 on a current tree.
- [ ] `... -- reset ...` performs the fast reset.
- [ ] Running it with PATH lacking `pwsh` (for example `$env:PATH = ($env:PATH -split ';' | ? { $_ -notmatch 'PowerShell' }) -join ';'` in a child process) prints the pwsh hint and exits 2.
- [ ] The script's exit code passes through: a stale tree with `reset` exits 1 with the script's message visible.

## Notes
- Follow `tools/local-scenarios/HeldRunSeeder` for the project layout and solution folder. Its `Git` helper shows the process-launch pattern (`ArgumentList`, `UseShellExecute = false`).
- Use `OutputDataReceived`/`ErrorDataReceived` or two read loops, and flush per line. Aspire captures the child's stdout and stderr as the resource's console log.
- Use `InternalsVisibleTo` for the test project if the helpers are internal.
- Do not reference Aspire or ServiceDefaults packages. The tool must stay runnable by hand.
