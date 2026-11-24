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
