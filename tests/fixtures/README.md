# Test Fixtures

Fixtures are controlled projects used by tests to prove project-system behavior.

Keep fixtures small and intentionally weird:

- single SDK project;
- multi-project references;
- Blazor/Razor component shape;
- WinForms partial/designer shape;
- conditional compilation;
- generated-code boundaries.

Fixtures are not samples. They exist to catch regressions.
