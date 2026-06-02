---
status: deferred
type: finding
created: 2026-06-01
---

# Source File Purpose Markers

## Summary

Earlier monitor work used source-file version markers and file-purpose attributes/comments as local memory inside source files. AIMonitor does not currently carry that convention forward.

The current documentation model feels strong: `README.md`, host files, system memory, component memory, feature maps, workflow docs, skills, and findings are easier to read than typical generated-code documentation and many human-written repo docs. Keep that document-first feel.

## Open Question

Should AIMonitor reintroduce source-file purpose/version markers?

Potential value:

- quick orientation when opening a file directly;
- local ownership hints near implementation code;
- extra friction against accidental contract-breaking edits;
- easier review of generated or AI-touched source.

Potential downside:

- noisy source headers;
- duplicate truth if component docs already own the purpose/data-flow story;
- churn when source purpose evolves;
- another convention agents must preserve.

## Current Lean

Do not reintroduce this tonight. Reconsider after a dead/duplicate/misplaced-code review, when there is better evidence about whether component docs alone are enough.

If reintroduced, keep it minimal and avoid turning every file into a documentation surface. Prefer a tiny purpose marker only where source ownership is otherwise unclear.

## Related Notes

GitHub Actions CI remains intentionally removed for now. The available evidence showed it running tests already run locally, and one CI failure came from an environment/test issue rather than a clear added safety signal. Reconsider CI only if there is sufficient evidence that it catches meaningful failures beyond the local build/test workflow.
