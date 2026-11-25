# -*- coding: utf-8 -*-
"""Reviewed explicit solid bounds query. No writes, file operations or arbitrary code."""
from pyrevit.api import DB
if doc is None or doc.IsFamilyDocument or doc.IsWorkshared or doc.IsReadOnly or doc.IsModelInCloud:
    raise ValueError("Expected local fixture")
if doc.PathName.lower() != r"C:\Users\naose\AppData\Local\KalidisAIEngine\Sandbox\Smoke-9c3c99fd336746a0b555995ee2728a38.rvt".lower():
    raise ValueError("Wrong document")
if set(inputs.keys()) != set(["elements"]):
    raise ValueError("Unexpected input")
items=inputs["elements"]
if not isinstance(items,list) or not 1<=len(items)<=12:
    raise ValueError("1-12 explicit elements required")
seen=set()
rows=[]
opt=DB.Options()
opt.DetailLevel=DB.ViewDetailLevel.Fine
opt.IncludeNonVisibleObjects=False
for item in items:
    if set(item.keys()) != set(["id","uniqueId"]) or item["id"] in seen:
        raise ValueError("Invalid explicit scope")
    seen.add(item["id"])
    e=doc.GetElement(DB.ElementId(int(item["id"])))
    if e is None or e.UniqueId!=item["uniqueId"] or isinstance(e,DB.ElementType) or isinstance(e,DB.RevitLinkInstance):
        raise ValueError("Element identity or scope mismatch")
    solids=[]
    def walk(geometry,depth):
        if depth>12:
            raise ValueError("Geometry nesting limit")
        if geometry is None:
            return
        for g in geometry:
            if isinstance(g,DB.GeometryInstance):
                walk(g.GetInstanceGeometry(),depth+1)
            elif isinstance(g,DB.Solid) and g.Volume>1e-9:
                if len(solids)>=100:
                    raise ValueError("Solid count limit")
                b=g.GetBoundingBox()
                pts=[b.Transform.OfPoint(DB.XYZ(x,y,z)) for x in [b.Min.X,b.Max.X] for y in [b.Min.Y,b.Max.Y] for z in [b.Min.Z,b.Max.Z]]
                coords=[[p.X*304.8,p.Y*304.8,p.Z*304.8] for p in pts]
                gs=doc.GetElement(g.GraphicsStyleId)
                solids.append({"minMm":[min(p[i] for p in coords) for i in range(3)],"maxMm":[max(p[i] for p in coords) for i in range(3)],"volumeM3":g.Volume*0.028316846592,"style":gs.GraphicsStyleCategory.Name if isinstance(gs,DB.GraphicsStyle) else None})
    walk(e.get_Geometry(opt),0)
    lo=[min(s["minMm"][i] for s in solids) for i in range(3)] if solids else None
    hi=[max(s["maxMm"][i] for s in solids) for i in range(3)] if solids else None
    rows.append({"id":e.Id.Value,"uniqueId":e.UniqueId,"minMm":lo,"maxMm":hi,"dimensionsMm":[hi[i]-lo[i] for i in range(3)] if solids else None,"solids":solids,"volumeM3":sum(s["volumeM3"] for s in solids)})
result={"path":doc.PathName,"rows":rows}
