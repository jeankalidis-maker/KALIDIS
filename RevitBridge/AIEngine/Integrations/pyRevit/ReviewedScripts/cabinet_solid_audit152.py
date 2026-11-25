# -*- coding: utf-8 -*-
"""Read-only physical bounds audit of the dedicated FamFab cabinet fixture."""
from pyrevit.api import DB
if doc is None or not doc.PathName.endswith("Smoke-9c3c99fd336746a0b555995ee2728a38.rvt"):
    raise ValueError("Wrong fixture")
e=doc.GetElement(DB.ElementId(362324))
if e is None or e.UniqueId!="7df24621-5ace-4f31-a07b-2d25b620b62e-00058754":
    raise ValueError("Wrong instance")
opt=DB.Options()
opt.DetailLevel=DB.ViewDetailLevel.Fine
opt.IncludeNonVisibleObjects=False
solids=[]
def walk(geometry):
    for g in geometry:
        if isinstance(g, DB.GeometryInstance):
            walk(g.GetInstanceGeometry())
        elif isinstance(g, DB.Solid) and g.Volume>1e-9:
            b=g.GetBoundingBox()
            pts=[b.Transform.OfPoint(DB.XYZ(x,y,z)) for x in [b.Min.X,b.Max.X] for y in [b.Min.Y,b.Max.Y] for z in [b.Min.Z,b.Max.Z]]
            coords=[[p.X*304.8,p.Y*304.8,p.Z*304.8] for p in pts]
            styles=set()
            for face in g.Faces:
                style=doc.GetElement(face.MaterialElementId)
            gs=doc.GetElement(g.GraphicsStyleId)
            solids.append({"minMm":[min(p[i] for p in coords) for i in range(3)],"maxMm":[max(p[i] for p in coords) for i in range(3)],"volumeM3":g.Volume*0.028316846592,"style":gs.GraphicsStyleCategory.Name if isinstance(gs,DB.GraphicsStyle) else None})
walk(e.get_Geometry(opt))
if len(solids)!=6:
    raise ValueError("Unexpected solids")
size=[max(s["maxMm"][i] for s in solids)-min(s["minMm"][i] for s in solids) for i in range(3)]
result={"id":e.Id.Value,"dimensionsMm":size,"solids":solids,"volumeM3":sum(s["volumeM3"] for s in solids)}
