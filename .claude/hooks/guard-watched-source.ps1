#requires -Version 5
# AIMonitor watched-source guard (Claude Code PreToolUse hook).
#
# Blocks the agent's NATIVE Edit/Write/NotebookEdit (and, when AIMONITOR_GUARD_DESTRUCTIVE_BASH=1, destructive shell
# commands) whose target resolves under the CURRENTLY watched solution root. The root is derived LIVE from
# config/appsettings.json on every call, because the watched solution is intentionally swappable (see
# docs/setup/LocalSetup.md and the WinForms "Choose..." button) -- a static deny entry would silently go stale on a swap.
#
# This only constrains Claude's built-in tools. AIMonitor's own MCP tools are already structurally confined to
# monitor-owned Working/Staged files, so they are not affected. Watched source must be changed through the workflow:
# refresh_file/new_file -> edit the Working candidate -> stage_candidate_for_review -> launch_staged_diff ->
# record_diff_decision.
#
# Fail-open by design: if the watched root cannot be determined, the hook allows the call (its job is to guard the
# watched root specifically, not to brick the session).

$ErrorActionPreference = 'Stop'

function Allow { exit 0 }
function Block($message)
{
    [Console]::Error.WriteLine($message)
    exit 2
}

# 1. Read the PreToolUse payload from stdin.
$raw = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($raw)) { Allow }
try { $payload = $raw | ConvertFrom-Json } catch { Allow }

$toolName = [string]$payload.tool_name
$toolInput = $payload.tool_input

# 2. Resolve the watched solution root from config (fail open if indeterminate).
$projectDir = $env:CLAUDE_PROJECT_DIR
if ([string]::IsNullOrWhiteSpace($projectDir)) { $projectDir = (Get-Location).Path }
$configPath = Join-Path $projectDir 'config/appsettings.json'
if (-not (Test-Path $configPath)) { Allow }
try { $config = Get-Content -Raw -Path $configPath | ConvertFrom-Json } catch { Allow }

$watchedSolution = $null
if ($config.Monitor -and $config.Monitor.WatchedSolutionPath)
{
    $watchedSolution = [string]$config.Monitor.WatchedSolutionPath
}
if ([string]::IsNullOrWhiteSpace($watchedSolution)) { Allow }

$watchedRoot = Split-Path -Parent $watchedSolution
if ([string]::IsNullOrWhiteSpace($watchedRoot)) { Allow }
try { $watchedRootFull = [System.IO.Path]::GetFullPath($watchedRoot).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar }
catch { Allow }

function IsUnderWatchedRoot($candidate)
{
    if ([string]::IsNullOrWhiteSpace($candidate)) { return $false }
    try { $full = [System.IO.Path]::GetFullPath($candidate) } catch { return $false }
    return $full.StartsWith($watchedRootFull, [System.StringComparison]::OrdinalIgnoreCase)
}

$guidance = "BLOCKED by AIMonitor watched-source guard. '$watchedRootFull' is the live watched solution root " +
    "(config/appsettings.json). Do not edit watched source with native tools. Route the change through the AIMonitor " +
    "MCP workflow: refresh_file/new_file -> edit the Working candidate -> stage_candidate_for_review -> " +
    "launch_staged_diff -> record_diff_decision."

# 3. Decide.
if ($toolName -eq 'Edit' -or $toolName -eq 'Write' -or $toolName -eq 'NotebookEdit')
{
    $target = $toolInput.file_path
    if (-not $target) { $target = $toolInput.notebook_path }
    if (IsUnderWatchedRoot $target) { Block $guidance }
    Allow
}

if ($toolName -eq 'Bash' -or $toolName -eq 'PowerShell')
{
    # Opt-in: destructive-command blocking only when explicitly enabled, to avoid surprising legitimate cleanup.
    if ($env:AIMONITOR_GUARD_DESTRUCTIVE_BASH -ne '1') { Allow }
    $cmd = [string]$toolInput.command
    if ([string]::IsNullOrWhiteSpace($cmd)) { Allow }
    $destructive = $cmd -match '(?i)(rm\s+-[a-z]*r|remove-item\b[^|;]*-recurse|rmdir\s+/s|del\s+/[a-z]*s|git\s+clean\s+-[a-z]*f)'
    # Normalize slashes so a forward-slash command path still matches the backslash watched root.
    $rootBare = $watchedRootFull.TrimEnd('\', '/').Replace('/', '\')
    $cmdNorm = $cmd.Replace('/', '\')
    if ($destructive -and ($cmdNorm -match [regex]::Escape($rootBare)))
    {
        Block "BLOCKED: refusing a destructive command that targets the watched solution root '$watchedRootFull'. Out-of-band recursive deletes of watched source are not allowed."
    }
    Allow
}

Allow
