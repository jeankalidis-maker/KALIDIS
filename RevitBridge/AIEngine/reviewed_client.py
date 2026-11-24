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
