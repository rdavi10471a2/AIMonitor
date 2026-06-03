# Codex CLI Workflow Test Samples

These samples are for the Codex-facing CLI workflow. They are not the Claude MCP path and they do not require the WinForms MCP bridge.

The samples use the committed `CodexWindows` baseline by default, but each run copies that baseline into `runtime/codex-workflow-samples/<run-id>/` and points AIMonitor at the copy. This keeps the committed sample clean while still testing the real watched-source workflow.

## Setup

Build the CLI first:

```powershell
dotnet build .\src\AIMonitor.Cli\AIMonitor.Cli.csproj
```

Create a disposable watched-solution copy and temporary config:

```powershell
$Repo = (Resolve-Path .).Path
$RunId = Get-Date -Format "yyyyMMddHHmmss"
$Scratch = Join-Path $Repo "runtime\codex-workflow-samples\$RunId"
$WatchedCopy = Join-Path $Scratch "CodexWindows"
$Config = Join-Path $Scratch "appsettings.codex-workflow-sample.json"
$Cli = Join-Path $Repo "src\AIMonitor.Cli\bin\Debug\net10.0\AIMonitor.Cli.dll"

New-Item -ItemType Directory -Force -Path $Scratch | Out-Null
Copy-Item -Recurse -Force `
    -Path (Join-Path $Repo "samples\watched-solutions\CodexWindows") `
    -Destination $WatchedCopy

@{
    Monitor = @{
        WatchedSolutionPath = Join-Path $WatchedCopy "CodexWindows.slnx"
        RuntimeRoot = "runtime"
        WinMergeCandidatePaths = @(
            "C:\Program Files\WinMerge\WinMergeU.exe",
            "C:\Program Files (x86)\WinMerge\WinMergeU.exe"
        )
    }
} | ConvertTo-Json -Depth 5 | Set-Content -Path $Config -Encoding UTF8

$AppConfig = Join-Path $WatchedCopy "AppConfig\AppConfig.cs"
```

Optional sanity check:

```powershell
dotnet $Cli index rebuild --repo-root $Repo --config $Config
dotnet build (Join-Path $WatchedCopy "WorkflowHarnessSample.slnx")
```

## Existing File Replace-Text Flow

This is the core Codex path: refresh a watched file into Working, apply an exact replacement through the CLI, stage it, launch WinMerge, and record the human decision.

```powershell
dotnet $Cli edit refresh --file $AppConfig --repo-root $Repo --config $Config

dotnet $Cli edit replace-text `
    --file $AppConfig `
    --old-text-file (Join-Path $Repo "samples\codex-workflow-tests\snippets\appconfig-old.txt") `
    --new-text-file (Join-Path $Repo "samples\codex-workflow-tests\snippets\appconfig-new.txt") `
    --expected-matches 1 `
    --repo-root $Repo `
    --config $Config

dotnet $Cli edit status --file $AppConfig --repo-root $Repo --config $Config

$StageJson = dotnet $Cli edit stage `
    --file $AppConfig `
    --ledger-summary "Codex sample existing-file replace-text" `
    --repo-root $Repo `
    --config $Config
$Stage = $StageJson | ConvertFrom-Json

dotnet $Cli edit launch-diff `
    --staged-record-id $Stage.stagedRecordId `
    --repo-root $Repo `
    --config $Config
```

In WinMerge, save the staged candidate into the watched copy if accepting. Then record:

```powershell
dotnet $Cli edit record-decision `
    --staged-record-id $Stage.stagedRecordId `
    --decision accepted `
    --expected-staged-hash $Stage.stagedHash `
    --repo-root $Repo `
    --config $Config
```

If rejecting, do not save in WinMerge and record:

```powershell
dotnet $Cli edit record-decision `
    --staged-record-id $Stage.stagedRecordId `
    --decision rejected `
    --repo-root $Repo `
    --config $Config
```

After an accepted or accepted-normalized decision, refresh before touching the same file again:

```powershell
dotnet $Cli edit refresh --file $AppConfig --repo-root $Repo --config $Config
```

## New File Add/Clean Flow

This flow creates a future watched file, writes a full class candidate into the monitor-owned Working file, accepts it through WinMerge, then repeats the flow with a cleaned version that removes every `_removed` member.

```powershell
$FutureFile = Join-Path $WatchedCopy "AppConfig\CodexWorkflowProbe.cs"

$NewJson = dotnet $Cli edit new --file $FutureFile --repo-root $Repo --config $Config
$New = $NewJson | ConvertFrom-Json
Copy-Item `
    -Path (Join-Path $Repo "samples\codex-workflow-tests\candidates\CodexWorkflowProbe.with-pairs.cs") `
    -Destination $New.workingFilePath `
    -Force

$StageAddJson = dotnet $Cli edit stage `
    --file $FutureFile `
    --ledger-summary "Codex sample new file with removable member pairs" `
    --repo-root $Repo `
    --config $Config
$StageAdd = $StageAddJson | ConvertFrom-Json

dotnet $Cli edit launch-diff `
    --staged-record-id $StageAdd.stagedRecordId `
    --repo-root $Repo `
    --config $Config
```

Accept in WinMerge by creating/saving the future watched file, then:

```powershell
dotnet $Cli edit record-decision `
    --staged-record-id $StageAdd.stagedRecordId `
    --decision accepted `
    --expected-staged-hash $StageAdd.stagedHash `
    --repo-root $Repo `
    --config $Config
```

Now clean the `_removed` members:

```powershell
$RefreshJson = dotnet $Cli edit refresh --file $FutureFile --repo-root $Repo --config $Config
$Refresh = $RefreshJson | ConvertFrom-Json
Copy-Item `
    -Path (Join-Path $Repo "samples\codex-workflow-tests\candidates\CodexWorkflowProbe.cleaned.cs") `
    -Destination $Refresh.workingFilePath `
    -Force

$StageCleanJson = dotnet $Cli edit stage `
    --file $FutureFile `
    --ledger-summary "Codex sample remove _removed member pairs" `
    --repo-root $Repo `
    --config $Config
$StageClean = $StageCleanJson | ConvertFrom-Json

dotnet $Cli edit launch-diff `
    --staged-record-id $StageClean.stagedRecordId `
    --repo-root $Repo `
    --config $Config
```

Accept in WinMerge, then:

```powershell
dotnet $Cli edit record-decision `
    --staged-record-id $StageClean.stagedRecordId `
    --decision accepted `
    --expected-staged-hash $StageClean.stagedHash `
    --repo-root $Repo `
    --config $Config
```

## Cleanup

The watched solution is disposable. Remove the copied sample run when done:

```powershell
Remove-Item -Recurse -Force $Scratch
```

Do not delete artifacts from a real watched project as part of this sample. This cleanup is safe only because the sample watched project lives under `runtime/codex-workflow-samples/<run-id>/`.

## Blazor Variant

Use `CodexBlazor` when the task needs Razor-shaped files instead of WinForms-shaped files. The same disposable-copy pattern applies; swap these setup values:

```powershell
$WatchedCopy = Join-Path $Scratch "CodexBlazor"
Copy-Item -Recurse -Force `
    -Path (Join-Path $Repo "samples\watched-solutions\CodexBlazor") `
    -Destination $WatchedCopy

# In the temporary config:
# WatchedSolutionPath = Join-Path $WatchedCopy "CodexBlazor.slnx"
```

Good first files for Codex CLI workflow trials:

- `Components\Pages\CodexDashboard.razor` for text/markup diff flow.
- `Components\Pages\CodexDashboard.razor.cs` for indexed C# code-behind flow.
- `Program.cs` for regular C# source-map/index behavior.
