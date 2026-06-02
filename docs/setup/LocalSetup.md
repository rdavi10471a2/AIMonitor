# Local Setup

1. Build the solution.

   ```powershell
   dotnet build .\AIMonitor.slnx
   ```

2. Copy `config/appsettings.template.json` to `config/appsettings.json`.

3. Set `Monitor:WatchedSolutionPath` to the absolute watched solution path, and keep `Monitor:WinMergeCandidatePaths` pointed at the local WinMerge executable candidates.

   The WinForms app can also set this value with `Choose...`. `config/appsettings.json` is local and ignored, so one AIMonitor checkout can switch between watched solutions without cloning the monitor for each target.

4. Run tests.

   ```powershell
   dotnet test .\AIMonitor.slnx
   ```

AIMonitor exposes one Monitor-facing workflow engine. MCP and CLI adapters should both call into that engine.
