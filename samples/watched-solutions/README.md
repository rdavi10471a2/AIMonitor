# Watched Solution Samples

Samples are human-readable projects used to demonstrate AIMonitor behavior.

Watched solution copies are local-only by default. Put large or real-world samples
under this folder when you want a local smoke target, but do not commit those
project copies. Until a smoke runner consumes sample-root config directly, point
CLI commands or smoke tasks at the solution path you want to exercise.

Example roots:

- `C:\SchemaStudioWebViewer`
- `C:\Source\USExcomManager`
- `C:\VSCodeProjects\AIMonitor\samples\watched-solutions\BlazorDetectorSample`

Do not use samples as the only regression proof. If a behavior must stay fixed, add a test fixture or smoke.

For Razor samples, keep smoke expectations representative and grep-verified. The current monitor indexes normal C#, clean `.razor.cs`, and source-mapped Razor references; it does not attempt to prove every Blazor markup binding in a production page.
