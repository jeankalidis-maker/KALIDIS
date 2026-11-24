# Kalidis AI Engine — release and recovery gates

Status: development branch ai-engine-v1, milestone 130. Not a production release.

## Before deployment
- Work in the dedicated local Sandbox copy. Identify the active document and process. Do not save experimental overlap fixtures into a real project.
- Resolve open dialogs manually and reconcile any unknown operation using independent inspection; never repeat a timed-out write blindly.
- Build Release for Revit 2025 / .NET 8. Review the build warnings and preserve the currently working DLL, manifest and test model.
- Use the existing authorized Stage-Engine/Update-Engine installation workflow and its hash verification/backup. Windows execution policy was Restricted. Do not bypass the policy; have the administrator approve the installation path. Restart Revit normally after saving only approved documents.
- Verify loaded version and capabilities via the supported MCP client. Confirm project_context, cache invalidation, revision guard, cancellation and the new curtain_wall command with independent readback. Roll back deployment if smoke tests fail.

## Acceptance of curtain_wall
- Use a local test RVT and a confirmed Curtain WallType with compatible default panel/grid configuration.
- Preview a straight wall with no explicit grids, verify panels and rollback. Test explicit U/V grid requests in a separate preview; verify generated native grid IDs and actual geometry.
- Commit a reviewed case only when preview succeeds. Independently audit wall/grid/panel/mullion IDs and inspect the model visually.
- Test invalid lengths, points, duplicate grids and type mismatch; verify failure does not leave created elements. Native failure preprocessor is not yet validated in a loaded new DLL.
- Mullion assignment, panel replacement and curtain doors remain separate work.

## Recovery and release
- Treat started/unknown journal entries as unresolved, even when the UI becomes responsive. Verify IDs, identities, geometry and dependencies, then explicitly decide whether a compensating operation is safe. Never replay automatically.
- A rollback preview with additional dependent deletions is a stop condition, not permission to delete those elements.
- The published ChatGPT connector must independently pass initialization, listing and authorized model calls. Previous SSE 429 is unresolved; do not claim direct ChatGPT connector availability from local stdio success.
- Run all JS suites, C# cache/cancellation tests, catalog matrix, real native tests and backup/restore acceptance. Confirm result with the user. See PENDENCIAS-UNIFICADAS.md.

## Milestone 137 — current local adapter operations
- Active DLL: SHA256 6AF4B084F19E9BADEE20D39A6283A69D4DD901A7AAFCDADEE0BCD0A41E65572F. The active local MCP advertises 130 tools. Changes in this milestone are adapter-side; no new DLL deployment required.
- engine_health: authenticated queue counters without a Revit transaction. Busy is a refusal, not permission to retry writes. A cooperative cancellation may complete as an error rather than increment the queue canceled counter.
- context_checkpoint: save a named local Sandbox checkpoint with optional explicit IDs; verify from a different adapter process. Any changed session, revision, selection, view or tracked identity is a reason to inspect; it is not a concurrency lock or write authorization. New batch writes use expectedSession and expectedRevision together.
- operation_receipt_review: recover a completed creation batch receipt scoped by path digest and expectedSession; verify current ID presence. An unknown/started operation, a missing scoped receipt, or a mismatch requires manual inspection. Never replay an uncertain request or infer that its effects were rolled back.
- Pinned catalogue: inventory -> independent family_catalog_audit start/status -> family_catalog_load start/status. Load requires exact file/hash/license/folder, matching audit and explicit commit/permit. A persistent project-path/source-hash reservation refuses repeat loads, including after a failed or unknown external event. Review an existing reservation with the owner; do not delete it automatically. Generic local RFA loading is not global catalogue deduplication. Check nested dependencies and version compatibility separately.
- UI: UIProbe is observational only. Use the reviewed native activate_view and zoom_fit operations on an identified test document/view and verify context/window title. Internal views cannot necessarily be activated. Never dismiss security/confirmation dialogs automatically.
- MATRIX-LIVE-137.json: 37 read-only probes passed; 120 sequential and 100 concurrent read requests, zero unexpected failures. 91 of 128 tools were not retested during that matrix; final catalogue increased to 130. This is not large-model or sustained-load qualification.
- The published ChatGPT MCP remains separate from the local stdio adapter; the earlier SSE 429 is not resolved by local validation.
