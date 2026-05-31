# MSBuild-First Indexing

V2 starts from MSBuild because filesystem enumeration was not enough.

The MSBuild layer owns:

- solution and project loading;
- project identity;
- compile items;
- target frameworks;
- conditional symbols;
- project references;
- package/reference context;
- workspace diagnostics.

Indexing must consume MSBuild-loaded projects rather than inventing project membership from folders.

## Initial Regression

`AIMonitor.MSBuild.Tests` opens a generated SDK-style project through `MSBuildWorkspace`. This is the first heartbeat test for the V2 direction.
