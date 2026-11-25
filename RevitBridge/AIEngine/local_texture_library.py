"""Index installed Autodesk texture assets and stage selected local samples.
No redistribution, network access, or modification of installed assets.
"""
from pathlib import Path
from hashlib import sha256
from uuid import uuid4
from PIL import Image
import os,json

ROOT=Path(os.environ.get("CommonProgramFiles",r"C:\Program Files\Common Files"))/"Autodesk Shared"/"Materials"/"Textures"

def search(query="",limit=100):
    if not 1<=limit<=10000:raise ValueError("Invalid limit")
    words=query.casefold().split()
    files=sorted(p for p in ROOT.rglob("*") if p.is_file() and p.suffix.lower() in (".png",".jpg",".jpeg") and not p.is_symlink() and all(w in p.name.casefold() for w in words))
    return dict(total=len(files),items=[str(p.relative_to(ROOT)) for p in files[:limit]],truncated=len(files)>limit)

def stage(relative,sandbox):
    root=ROOT.resolve(strict=True)
    src=(root/relative).resolve(strict=True)
    if not src.is_relative_to(root) or src.suffix.lower() not in (".png",".jpg",".jpeg") or not src.is_file() or src.stat().st_size>10_000_000:raise ValueError("Invalid installed image")
    sandbox=Path(sandbox)
    if sandbox.name!="Sandbox" or not sandbox.is_dir() or sandbox.is_symlink():raise ValueError("Dedicated sandbox required")
    dst=sandbox/("TextureFixture-"+uuid4().hex+".png")
    with Image.open(src) as img:
        if img.format not in ("PNG","JPEG") or max(img.size)>4096 or min(img.size)<16:raise ValueError("Unsupported source")
        size=list(img.size)
        with dst.open("xb") as f:img.convert("RGB").save(f,format="PNG")
    return dict(source=str(src),sourceSha256=sha256(src.read_bytes()).hexdigest(),fileName=dst.name,sha256=sha256(dst.read_bytes()).hexdigest(),sizePx=size,note="Local installed asset; not redistributed. Scale must be selected explicitly.")

def create_material(engine,p,relative,source_id,name,x_mm,y_mm):
    from reviewed_client import execute
    info=stage(relative,Path(p).parent)
    a=dict(id=source_id,name=name+" Bitmap "+uuid4().hex[:8],fileName=info["fileName"],expectedSha256=info["sha256"])
    dry=execute(engine,"bitmap_material",a,p);assert dry["dryRun"]
    b=execute(engine,"bitmap_material",a,p,commit=True);assert b["status"]=="Committed"
    a=dict(mode="duplicate",id=b["result"]["id"],name=name+" "+uuid4().hex[:8],sizeXmm=x_mm,sizeYmm=y_mm)
    dry=execute(engine,"texture_scale",a,p);assert dry["dryRun"]
    r=execute(engine,"texture_scale",a,p,commit=True);assert r["status"]=="Committed"
    return dict(asset=info,material=r["result"])
