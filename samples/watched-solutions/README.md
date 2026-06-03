# Watched Solution Samples

Samples are human-readable projects used to demonstrate AIMonitor behavior.

Watched solution copies are local-only by default. Put large or real-world samples
under this folder when you want a local smoke target, but do not commit those
project copies.

`WorkflowHarnessSample` is the exception: it is a committed, tiny watched
solution used by repeatable workflow smokes. Use
`config/appsettings.workflow-harness-sample.json` with CLI, MCP server, or a
WinForms app started with `--config config/appsettings.workflow-harness-sample.json`
when a test should avoid a developer's real watched application.

Example roots:

- `C:\SchemaStudioWebViewer`
- `C:\Source\USExcomManager`
- `C:\VSCodeProjects\AIMonitor\samples\watched-solutions\BlazorDetectorSample`
- `C:\VSCodeProjects\AIMonitor\samples\watched-solutions\WorkflowHarnessSample`

Do not use samples as the only regression proof. If a behavior must stay fixed, add a test fixture or smoke.

For Razor samples, keep smoke expectations representative and grep-verified. The current monitor indexes normal C#, clean `.razor.cs`, and source-mapped Razor references; it does not attempt to prove every Blazor markup binding in a production page.
