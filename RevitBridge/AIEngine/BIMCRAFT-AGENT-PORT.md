# AI-agent architecture: BIMCraft and pyRevit integration (140)

## BIMCraft
Pinned upstream IPedrax/bimcraft 73aceeae81efdece268de404644a2b72822ce408. Reviewed apps/bimcraft-cli/src/mcp.rs, crates/core/src/commands.rs, and dry_run_tests.rs. BIMCraft is a separate Rust BIM modeler; we adopt its agent-first command design, not its geometry kernel or its on-disk format. Revit remains the native authoring engine.

| Upstream concept | Kalidis implementation | State / limits |
|---|---|---|
| One catalog and schema per command | command-registry, agent_command_catalog | 139 live tools, 58 action schemas; active capability filter |
| Script with command and JSON per line | agent_plan | 15 creation commands, 1-100 lines, prior aliases |
| Atomic script | native batch transaction | verified on seven-element room fixture; not a global transaction for files/external tools |
| Dry run | native rollback preview, persistent digest receipt | existence and transaction status, not visual/geometry equivalence |
| Context of active desktop | project_context, context_checkpoint | local saved Sandbox RVT, revision and session checks |
| Recovery / audit | operation journal, operation_receipt_review | no replay of unknown outcomes |
| Undo/redo | reversible_move, reversible_pin, structured comment workflow | limited compensating edits, not global history |
| Agent feedback | native geometry/solid/clash/preview tools | scope-specific independent measurement |
| Plugin commands | approved native/reviewed tool registry | no arbitrary full-trust scripts or auto-loading foreign code |

Example script:
```
wall {"levelId":311,"typeId":396,"start":{"x":240000,"y":190000},"end":{"x":246000,"y":190000},"heightMm":3000,"alias":"south"}
hosted_component {"levelId":311,"typeId":350822,"hostId":"$south","point":{"x":243000,"y":190000,"z":0},"alias":"door"}
```
IDs shown are Smoke fixtures only. Query the active model and use new approved geometry. Call agent_plan mode preview, then apply with identical script, planId, expectedPath and returned digest. The digest is a content/revision binding, not a human authorization token. The adapter uses requestId for both preview and apply. It refuses changed scripts, stale revision, new session, reused plan IDs, arbitrary commands or external-file effects. Only hosted components on an earlier alias are accepted. Native rollback currently increments the document revision once; unexpected transitions are refused. Autojoins/implicit Revit dependencies can affect existing elements; the explicit creation inventory is not an exhaustive side-effect diff. Inspect the real geometry and effects before saving. No automatic retry or undo.

## pyRevit
Official release v7.1.0.26319+1907 (15 Nov 2026) and https://github.com/pyrevitlabs/pyRevit/blob/develop/docs/agent-runtime.md reviewed. The release adds experimental local agent host using named pipe, with MCP stdio via pyrevit CLI. It exposes context, element inspection, API reflection, skills, visual capture, fixed automation, Python query and modify, dry run, and approval with native undo entry. Separate optional dependency; no GPL source transplanted into this engine.

Verified installer:
C:/KALIDIS/AIEngineDevelopment/UpstreamReview139/Installers/pyRevit_7.1.0.26319_signed.exe
SHA256 5F98862360DAADCF728617414816DF8406C19080E47847041DF62382A2577381, Authenticode Valid when checked 139. Install interactively with Revit closed and verify the attachment targets Revit 2025. Keep agent host off until the test project is identified; enable it only through supported pyRevit settings with policy ask, reckless off, and no automatic modification approval. First test pyrevit agent status and context, then inspect one sandbox element and run the Kalidis read-only extension. The official readonly policy restricts model changes, NOT filesystem/network/process access from Python; do not present arbitrary Python as sandboxed. Restrict Kalidis-side bridge to fixed inspected operations; use normal native confirmation for any modification. No external agent connector has yet been registered. Do not imply that pyrevit mcp install codex connects ChatGPT's normal chat. The published Kalidis MCP 429 remains a separate issue.

The next DLL is already compiled from 139 (D08CA9ED...); staging and restarting is required for the five additional native KenLP ports and grouped cuts. 140 changes are adapter-only and live.
