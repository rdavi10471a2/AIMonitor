# Local Setup

1. Build the solution.

   ```powershell
   dotnet build .\AIMonitor.slnx
   ```

2. Copy `config/appsettings.template.json` to `config/appsettings.json`.

3. Set `Monitor:WatchedSolutionPath` to the absolute watched solution path.

4. Run tests.

   ```powershell
   dotnet test .\AIMonitor.slnx
   ```

V2 exposes one Monitor-facing workflow engine. MCP and CLI adapters should both call into that engine.
