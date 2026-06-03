# Local Setup

1. Build the solution.

   ```powershell
   dotnet build .\AIMonitor.slnx
   ```

2. Copy `config/appsettings.template.json` to `config/appsettings.json`.

3. Set `Monitor:WatchedSolutionPath` to the absolute watched solution path, and keep `Monitor:WinMergeCandidatePaths` pointed at the local WinMerge executable candidates.

   The WinForms app can also set this value with `Choose...`. `config/appsettings.json` is local and ignored, so one AIMonitor checkout can switch between watched solutions without cloning the monitor for each target.

4. Enable the watched-source guard hook (Claude Code).

   The agent's own native `Edit`/`Write`/`NotebookEdit` tools sit outside the monitor engine, so AIMonitor's structural
   confinement does not cover them — only its own MCP tools. Wire the `PreToolUse` guard so native edits to the watched
   solution are blocked and routed through the workflow instead. Add this to `.claude/settings.json`:

   ```json
   "hooks": {
     "PreToolUse": [
       {
         "matcher": "Edit|Write|NotebookEdit|Bash|PowerShell",
         "hooks": [
           {
             "type": "command",
             "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"$CLAUDE_PROJECT_DIR\\.claude\\hooks\\guard-watched-source.ps1\""
           }
         ]
       }
     ]
   }
   ```

   The hook resolves the watched root **live** from `Monitor:WatchedSolutionPath` on every call, so it follows a
   `Choose...` solution swap and never goes stale (no per-machine paths to maintain). Destructive-shell blocking
   (`rm -rf`/`Remove-Item -Recurse`/`git clean -fd` under the watched root) is opt-in via the
   `AIMONITOR_GUARD_DESTRUCTIVE_BASH=1` environment variable. AIMonitor's own MCP tools are unaffected.

5. Run tests.

   ```powershell
   dotnet test .\AIMonitor.slnx
   ```

AIMonitor exposes one Monitor-facing workflow engine. MCP and CLI adapters should both call into that engine.
