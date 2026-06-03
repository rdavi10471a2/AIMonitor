---
status: open
type: note
created: 2026-06-03
audience: Codex + operator
scope: Claude's WinForms/Blazor watched samples + ClaudeSmokes gate; gaps surfaced; next steps
note: the samples AND the ClaudeSmokes TESTS are kept LOCAL (uncommitted) per operator preference; this is their record
---

# ClaudeSmokes — WinForms/Blazor samples + next steps (note for Codex)

## What Claude did this session (all LOCAL, tests-only, no `src/` edits)

Authored two **meaningful in-repo watched samples** (test fixtures, not monitor code) + a full mean-teacher ClaudeSmokes
gate, all verified green and grep-validated against the real sample source:

- `samples/watched-solutions/WinFormsSample` — `Customer` + `ICustomerRepository` + generic `RepositoryBase<T>` +
  `CustomerRepository` (override + interface impl) + `CustomerService` (call sites + method-body `new`) +
  `MainForm`/`MainForm.Designer.cs` (designer noise).
- `samples/watched-solutions/BlazorSample` — same repository trio + `CustomerList.razor` (`@code` + `@bind`) +
  `CustomerList.razor.cs` code-behind.

**ClaudeSmokes gate: 16 cases, all green** (Data 8 / Workflow 5 / Integration 3), `[Trait("Suite","ClaudeSmokes")]`.
New this batch: WinForms index graph + caller identity; WinForms source-map designer-noise collapse-with-marker;
materialize/backing-store (write-cycle on a copy leaves the committed sample byte-identical); Blazor index + repository
graph + `.razor` `@code` reference mapping; plus the in-repo `WorkflowHarnessSample` index smoke (CI-portable).

## Gaps the mean-teacher tests surfaced (for Codex — grep-validated, not theory)

1. **`overrides` relationship is not emitted for an override of a GENERIC base virtual method.**
   `CustomerRepository.GetByIdAsync` overrides `RepositoryBase<Customer>.GetByIdAsync`, but the index emits only
   `inherits_from` + `implements_interface_member`, no `overrides` row — whereas the Phase-1 non-generic fixture *did*
   emit `overrides`. So override-relationship extraction is inconsistent across generic vs non-generic bases.
   (Confirmed by dumping `ListRelationships()` for `WinFormsSample`.)
2. **Object-creation call sites are captured only for an EXPLICIT constructor invoked in a METHOD BODY.** A `new` in a
   field initializer (`private readonly X x = new X();`) produced no call site, and `new Customer()` produced none until
   `Customer` was given an explicit `public Customer() { }`. Implicit default ctors and field-initializer `new`s aren't
   recorded. Worth deciding whether to broaden (field initializers run in the ctor; implicit ctors are real targets).
3. **dirty-unexpected blocking is an orphaned status** (carried from Phases 2-5): the persisted
   `Status="dirty-unexpected"` is consumed by nothing; the file is protected only incidentally by the generic
   watched-changed guard, and the block message isn't dirty-unexpected-specific. Low/ergonomics.
4. **Live-smoke fixture indentation** (Phase 6): `--sample-workflow-harness`/`--mcp-live-syntax-rejection` `oldText`
   uses 8-space vs the sample's 4-space; harden when those live modes are made strict.

None of these are safety holes — the hard floor (watched-source immutability, pre-merge full-build gate, hash
classification) is intact and independently verified.

## Claude's next steps

- **Review + ClaudeSmoke Phase 7 (history/prune) and Phase 8 (closure) when pushed** — same drill: parity-verify
  `/workflow` (false-PASS + orphaned-field hunt) → author local ClaudeSmokes → push finding, keep tests local.
- The WinForms/Blazor samples are currently **local (Claude-authored)**. **Promoting them to committed is the one step
  that makes them CI-portable ground-truth for everyone** and lets the ClaudeSmokes drop the external
  `C:\SchemaStudioWebViewer`/`Schema Studio - DBV2` dependency — operator's call.

## Coordination

Codex owns all production fixes; Claude is the review + local ClaudeSmokes gate. Tests and samples stay local; only
findings/notes are pushed. The ClaudeSmokes authoring recipe (3 flavors + shapes/gotchas) is in Claude's session memory.
