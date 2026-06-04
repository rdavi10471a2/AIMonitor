---
status: open
type: review
created: 2026-06-04
audience: Codex + operator
scope: review of 4d1d392 "Add retrieval backups on watched file refresh"
reviewer: Claude (review gate)
context: positioned as one additional safety net before working on a real, can't-be-broken watched solution
---

# Review — retrieval backups on watched-file refresh (4d1d392)

## Verdict

Solid, safety-additive, well-tested, floor-consistent. On `Refresh` of an existing file it snapshots the **exact**
watched bytes (before the Working copy) to `runtime/watched-solutions/<id>/retrieval-backups/<rel>/<ts>-<hash>-<guid>.<ext>.bak`,
records path/hash/time on manifest+status, and `NewFile` correctly skips it. Scoped (per the system-memory README) as a
recovery aid that does not affect staging/validation/review/decision — additive, not floor-entangling. The right move as
an extra net before high-stakes watched work. Ship it; the items below are refinements.

**Strengths:** exact pre-refresh byte snapshot, hash-stamped; `overwrite:false` + guid → append-only, never clobbers
prior snapshots; clean layering (paths in `WorkflowEditPaths`, fields on manifest/status, logic in the service — no
adapter leak); and genuinely mean-teacher tests — byte-equality with the watched source (not shape-only), hash match,
and the negative (new-file → no backup, no directory).

## Flags

1. **Local recovery snapshot, not off-machine backup — lives in gitignored `runtime/`.** Fine *as an additional net*
   (protects against a bad refresh/edit cycle), but it shares the disk-loss fate of what it backs up — it does nothing
   for "the drive died." Don't let "backups" imply disaster recovery; off-machine durability is a separate concern
   (watched-repo remote / the docs-branch idea). Same storage tension flagged for the memory plan.

2. **Unbounded growth, no retention** — every refresh writes a new `.bak`, never deleted; the manifest tracks only the
   last, so older snapshots orphan and accrue (one full copy per refresh, forever; no prune — Phase 7 deferred).
   **Operator is aware and will manage retention.** Recorded for completeness.

3. **Backup is on the refresh critical path with no `try/catch` → a failure blocks refresh.** For a can't-be-broken
   target this is a **defensible fail-safe** (no recovery point captured → don't proceed). The residual concern is
   *robustness*, not the blocking: on Windows, a deep watched path (this solution's `Components/Pages/BaseViewGenerator/…`
   tree) plus the long runtime root and the ~40-char backup filename can hit `MAX_PATH` and throw `PathTooLongException`,
   which would block a *legitimate* refresh and look like a safety refusal. Add a path-length guard / shortened-backup-name
   fallback so a path-length failure can't masquerade as "refused for safety."

## Meta

This is the same runtime-vs-durable storage question as the memory plan, now in shipped code (Codex defaulted to
`runtime/` for recovery-ish content again). Worth deciding the **storage + retention contract once** for both retrieval
backups and the memory workspace, rather than twice.
