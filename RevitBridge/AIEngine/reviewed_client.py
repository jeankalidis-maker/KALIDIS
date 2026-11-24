"""Named reviewed operations over the existing authenticated Revit engine.
No discovery of arbitrary executable code, no automatic approval or retry.
"""
from pathlib import Path
from hashlib import sha256
import json

OPERATIONS = {
    "geometry_review": ("836DB468CE9D", True),
    "solid_audit": ("EC63193EB239", True),
    "verified_move": ("A391FFE06931", False),
    "straight_rebar": ("AB026746B032", False),
    "rebar_set": ("4A40E0822D70", False),
    "solid_clash": ("BBFBE7A06881", True),
    "family_type_flex": ("650AEC399153", False),
    "bent_rebar": ("7D9BBE6718C0", False),
    "mep_topology": ("FD0E425A19D2", True),
    "clearance_screen": ("FB3888758AE8", True),
    "mep_connect": ("459041107B73", False),
    "sloped_floor": ("D3B55A24150F", False),
    "material_takeoff": ("FB499380E8E7", True),
    "paint_top_face": ("B7158B02BAE8", False),
    "wall_attach_roof": ("14A6A6F23934", False),
    "rebar_inventory": ("18C56F5D6B28", True),
    "checked_wall_opening": ("3544CE89C7F1", False),
    "mep_split": ("138475486420", False),
    "view_review": ("1B9EB63F4F1F", False),
    "native_material_schedule": ("9B40E496AB51", False),
    "closed_stirrup": ("8C624632C082", False),
    "layer_audit": ("6AF2D4EA2171", True),
    "layer_variant": ("A91EADBEAC86", False),
    "sheet_review": ("B180A9C5F7F8", False),
    "material_appearance": ("427657519022", False),
    "schedule_review": ("847D785D8644", False),
    "profile_flex": ("D27CFDC5796F", False),
    "routing_variant": ("2E0D55B98E9B", False),
    "native_shapes": ("78FE61EDA875", False),
    "native_view_filter": ("EC334369A78F", False),
    "quality_checks": ("A63EE3E311C8", True),
    "hooked_stirrup": ("87824F47E848", False),
    "auto_clearance_repair": ("4683C3604887", False),
    "spatial_repair": ("C3890549057C", False),
    "room_repair": ("E03C1ABA1063", False),
    "polygon_repair": ("3DEA48D00B14", False),
    "linked_repair": ("A5FCACD151A1", False),
    "mep_clearance_repair": ("5B355A8649D6", False),
    "solid_profile_flex": ("D61A528E9FDE", False),
    "cylinder_flex": ("76808BA4814D", False),
    "bitmap_material": ("9CD033DBCAF7", False),
    "python_probe": ("00118D96D6D2", True),
    "python_comments": ("E91C635DCE63", False),
    "bim_file_review": ("54191E8ACEE5", True),
    "dynamo_audit": ("C4F5B1F8CB03", False),
    "raster_building": ("240E8B5DB189", False),
    "texture_scale": ("A9F833D4E0CE", False),
    "hooked_set": ("DA411F02960E", False),
    "wall_gap_repair": ("4DC9328597FA", False),
    "realistic_preview": ("4B5CDDD525F4", False),
    "network_clearance": ("FD86FEBABF57", False),
    "pipe_bypass": ("CF9F1BD198DD", False),
    "dynamo_write_fixture": ("77EDAEE70399", False),
    "family_quarantine_audit": ("A83AB9333383", False),
    "family_catalog_audit": ("F343C0BEDF84", False),
    "dynamo_wall_fixture": ("7BA04AE196ED", False),
}
def resolve(folder, operation):
    prefix, _ = OPERATIONS[operation]
    folder=Path(folder)
    approved=json.loads((folder/"approved.json").read_text(encoding="utf-8-sig"))
    matches=[]
    for name,digest in approved.items():
        if Path(name).name!=name or not name.startswith("Kalidis.Reviewed."+prefix+".") or not name.endswith(".dll"):
            continue
        p=folder/name
        if p.is_file() and not p.is_symlink() and sha256(p.read_bytes()).hexdigest().lower()==digest.lower():
            matches.append(name)
    if len(matches)!=1:
        raise RuntimeError("Expected exactly one approved matching binary; resolve missing or ambiguous approval")
    return matches[0]

def execute(engine, operation, arguments, expected_path, commit=False):
    _, read_only=OPERATIONS[operation]
    if operation=="dynamo_audit":
        if arguments.get("mode")=="start" and (not commit or arguments.get("executeReadOnlyGraph") is not True):raise ValueError("Dynamo start requires explicit commit and executeReadOnlyGraph=true; event cannot roll back")
        if arguments.get("mode")=="status" and commit:raise ValueError("Status must be rollback-only")
    event_permits={"dynamo_write_fixture":"executeFixtureGraph","family_quarantine_audit":"inspectPinnedFile","family_catalog_audit":"inspectPinnedFile","dynamo_wall_fixture":"executeFixtureGraph"}
    if operation in event_permits:
        if arguments.get("mode")=="start" and (not commit or arguments.get(event_permits[operation]) is not True):raise ValueError("External event requires explicit commit and permit; cannot roll back")
        if arguments.get("mode")=="status" and commit:raise ValueError("Status must be rollback-only")
    if read_only and commit:
        raise ValueError("Inspection must roll back")
    if engine.read_only:
        raise PermissionError("Extension execution requires explicit write-enabled session; even inspection uses rollback")
    dll=resolve(engine.home/"ReviewedExtensions", operation)
    if dll not in engine.call("extensions")["approved"]:
        raise RuntimeError("Extension not reported approved by live engine")
    return engine.call("run_extension",expectedPath=str(expected_path),assembly=dll,operation=operation,arguments=arguments,commit=commit)

if __name__=="__main__":
    from tempfile import TemporaryDirectory
    with TemporaryDirectory() as tmp:
        p=Path(tmp);name="Kalidis.Reviewed.836DB468CE9D.fixture.dll";(p/name).write_bytes(b"fixture")
        (p/"approved.json").write_text(json.dumps({name:sha256(b"fixture").hexdigest()}))
        assert resolve(p,"geometry_review")==name
        (p/name).write_bytes(b"changed")
        try:resolve(p,"geometry_review")
        except RuntimeError:pass
        else:raise AssertionError("Hash mismatch accepted")
    print("REVIEWED_CLIENT_TESTS=PASS (fixture)")
