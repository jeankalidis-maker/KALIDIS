# Plugin-first integration package 139

The Kalidis motor stays the single supported Revit gateway. Upstream source is reviewed at pinned revisions, and selected commands are adapted to its native transaction/readback contracts. No competing server was started.

## Ten complementary commands
| Tool | Origin / implementation | State |
|---|---|---|
| find_element_by_unique_id | KenLP FindElementByUniqueIdCommand, explicit host/link scope | compiled; restart required |
| list_materials | KenLP ListMaterialsCommand, paginated | compiled; restart required |
| list_sheets | KenLP ListSheetsCommand, actual title-block category | compiled; restart required |
| list_spaces | KenLP ListSpacesCommand, include unplaced spaces | compiled; restart required |
| list_rooms | KenLP ListRoomsCommand, include unplaced rooms | compiled; restart required |
| get_parameter | KenLP GetParameterCommand, exact ID or unambiguous name | live |
| get_element_properties | Mvclr tools/parameter_tools.py, separated type/instance | live |
| check_clearance | KenLP CheckClearanceCommand, conservative AABB gap | live |
| copy_text_parameter | KenLP CopyParametersCommand + structured batch concept, Comments only, explicit identity and prior values | live |
| legacy_geometry_snapshot | first-party COMANDOS.md snapshot_elemento, explicit IDs and solid audit | live |

References: pinned KenLP fdf6efa23e57fe59effb8191666d04b4f73142a0; Mvclr e09b6524f3fc431e65d84a7a1d42ee8da9456a99; AndreyStartsev 0af7eef16631b9db8f7fa8b5c06dbe06c9181d2a; Horizun 78a2a51aeea79666545dec6e01efda9f1e4b5a2c. Source-only clones in UpstreamReview139; no upstream executable run. Retain THIRD-PARTY notices. The new code is adapted, not full protocol or runtime parity.

## Next plugin-specific gates
- BIMCraft: map existing registry, context, receipts, compensating moves/pins and preview; fuller simulation and reversibility remain.
- KenLP: test five additional native reads after deploying the next DLL; verify explicit linked identity, pagination, empty spaces and level filtering.
- pyRevit: official 7.1.0.26319 release, 2026-11-15, per-user installer. Review signed installer and perform interactive installation with Revit closed; keep its agent runtime disabled until its approval and tool-scope model are reviewed. Validate original Kalidis audit bundle separately. No installation or attachment yet.
- Andrey: review example 03_batch_parameter_update.py; use bounded typed operations and approved fixed Python routines, not arbitrary Python or a parallel local server.
- Mvclr: properties port live; compare remaining useful transform, annotation and MEP commands before implementation.
- FamFab: dimensional table recipe and native forms verified in 138; image workflow and dependency/cost review still open.
- Horizun: Coherence.cs reviewed; port advisory support/connectivity findings with explicit incomplete coverage, not compliance certification.
- Dynamo: reviewed graphs only; broader graph construction and dependency validation remain.
- Legacy: geometry snapshot added; no claim of whole legacy protocol compatibility.

## Runtime observations
Five adapter commands validated on Smoke with independent native reads. Two furniture comments restored and saved. 137 active tools; five additional native commands are not advertised until new DLL loads. Build SHA256 D08CA9ED7A53B2B0C804B062B11830C1118DC974309E236A211C63AE645A291F; contains grouped family cuts from 138 too. Current loaded DLL remains 6AF4... . No original project modified.
