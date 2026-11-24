"""Readback report for explicit element IDs in a test project.
No automatic edits. Quantities are model quantities, not purchase or design certification.
"""
from reviewed_client import execute
from pathlib import Path
import json
from datetime import datetime,timezone
from uuid import uuid4

def engineering_audit(engine, path, *, materials, reinforcement, geometry):
    path=Path(path)
    if Path(engine.snapshot()["path"])!=path:
        raise RuntimeError("Active document changed")
    groups={"materials":("material_takeoff",materials),"reinforcement":("rebar_inventory",reinforcement),"geometry":("geometry_review",geometry)}
    result={"project":path.name,"timestampUtc":datetime.now(timezone.utc).isoformat(),"sections":{}}
    for name,(op,ids) in groups.items():
        if not ids:continue
        if len(ids)>100 or any(type(i) is not int or i<=0 for i in ids):
            raise ValueError("Explicit valid positive element IDs required")
        r=execute(engine,op,{"ids":list(dict.fromkeys(ids))},path)
        if not r.get("ok") or r.get("status")!="RolledBack":
            raise RuntimeError("Inspection transaction did not roll back")
        result["sections"][name]=r["result"]
    if Path(engine.snapshot()["path"])!=path:raise RuntimeError("Active document changed")
    result["note"]="Selected elements only; quantities do not certify structural adequacy, code compliance or procurement."
    return result

def save_report(path, report):
    root=Path(path).parent
    if not root.is_dir() or root.is_symlink() or root.name!="Sandbox":
        raise ValueError("Use test project sandbox")
    dest=root/("engineering-audit-"+uuid4().hex+".json")
    with dest.open("x",encoding="utf-8") as f:json.dump(report,f,ensure_ascii=False,indent=2,allow_nan=False)
    return dest
