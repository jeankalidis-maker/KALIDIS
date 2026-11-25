# -*- coding: utf-8 -*-
"""Fixed L-junction rehearsal; only the pyRevit dry_run agent may run it."""
from pyrevit.api import DB
from Autodesk.Revit.DB.Structure import StructuralWallUsage
import os
root=os.path.join(os.environ["LOCALAPPDATA"],"KalidisAIEngine","Sandbox")
if doc is None or doc.PathName.lower()!=os.path.join(root,"Smoke-9c3c99fd336746a0b555995ee2728a38.rvt").lower() or doc.IsModified or doc.IsWorkshared or doc.IsReadOnly or doc.IsFamilyDocument:
    raise ValueError("Saved Smoke fixture required")
level=doc.GetElement(DB.ElementId(311))
typ=doc.GetElement(DB.ElementId(398))
if not isinstance(level,DB.Level) or not isinstance(typ,DB.WallType) or typ.Kind!=DB.WallKind.Basic or abs(typ.Width*304.8-200)>0.1:
    raise ValueError("Fixture type mismatch")
def pt(x,y):
    return DB.XYZ(x/304.8,y/304.8,level.Elevation)
def make(a,b):
    w=DB.Wall.Create(doc,DB.Line.CreateBound(a,b),typ.Id,level.Id,1000/304.8,0,False,False)
    DB.WallUtils.DisallowWallJoinAtEnd(w,0)
    DB.WallUtils.DisallowWallJoinAtEnd(w,1)
    return w
def vol(w,v):
    result=0.0
    for s in w.get_Geometry(DB.Options()):
        if not isinstance(s,DB.Solid) or s.Volume<1e-9: continue
        for q in v.get_Geometry(DB.Options()):
            if not isinstance(q,DB.Solid) or q.Volume<1e-9: continue
            inter=DB.BooleanOperationsUtils.ExecuteBooleanOperation(s,q,DB.BooleanOperationsType.Intersect)
            result+=abs(inter.Volume)*0.028316846592
            inter.Dispose()
    return result
# Fixed isolated test region; reject any existing physical geometry in an expanded box.
a,b,c=pt(900000,300000),pt(904000,300000),pt(900000,304000)
outline=DB.Outline(pt(899000,299000)-DB.XYZ.BasisZ*2,pt(905000,305000)+DB.XYZ.BasisZ*6)
if DB.FilteredElementCollector(doc).OfClass(DB.Wall).WherePasses(DB.BoundingBoxIntersectsFilter(outline)).GetElementCount()!=0:
    raise ValueError("Test wall region occupied")
t=DB.Transaction(doc,"Kalidis L junction rehearsal")
t.Start()
try:
    w=make(a,b)
    v=make(a,c)
    doc.Regenerate()
    before=vol(w,v)
    if abs(before-.01)>1e-6:
        raise ValueError("Initial overlap mismatch {}".format(before))
    DB.WallUtils.AllowWallJoinAtEnd(w,0)
    DB.WallUtils.AllowWallJoinAtEnd(v,0)
    doc.Regenerate()
    joined=DB.JoinGeometryUtils.AreElementsJoined(doc,w,v)
    ends=[x.Id.Value for x in w.Location.get_ElementsAtJoin(0)]
    endjoin=v.Id.Value in ends
    after=vol(w,v)
    axes=w.Location.Curve.GetEndPoint(0).DistanceTo(a)<.1/304.8 and w.Location.Curve.GetEndPoint(1).DistanceTo(b)<.1/304.8 and v.Location.Curve.GetEndPoint(0).DistanceTo(a)<.1/304.8 and v.Location.Curve.GetEndPoint(1).DistanceTo(c)<.1/304.8
    reverseJoin=w.Id.Value in [x.Id.Value for x in v.Location.get_ElementsAtJoin(0)]
    if not axes or not (endjoin or reverseJoin) or after>1e-7:
        raise ValueError("Symmetric end joint or physical overlap failed")
    DB.WallUtils.DisallowWallJoinAtEnd(w,0)
    DB.WallUtils.DisallowWallJoinAtEnd(v,0)
    doc.Regenerate()
    separated=vol(w,v)
    if abs(separated-before)>1e-7 or v.Id.Value in [x.Id.Value for x in w.Location.get_ElementsAtJoin(0)] or w.Id.Value in [x.Id.Value for x in v.Location.get_ElementsAtJoin(0)]:
        raise ValueError("Separate readback failed")
    DB.WallUtils.AllowWallJoinAtEnd(w,0)
    DB.WallUtils.AllowWallJoinAtEnd(v,0)
    doc.Regenerate()
    rejoined=vol(w,v)
    if rejoined>1e-7 or not (v.Id.Value in [x.Id.Value for x in w.Location.get_ElementsAtJoin(0)] or w.Id.Value in [x.Id.Value for x in v.Location.get_ElementsAtJoin(0)]):
        raise ValueError("Rejoin readback failed")
    # Always explicitly roll back, even within the agent group; do not commit a warning.
    result={"ids":[w.Id.Value,v.Id.Value],"beforeM3":before,"afterM3":after,"endJoin":endjoin,"geometryJoin":joined,"axesPreserved":axes,"separatedM3":separated,"rejoinedM3":rejoined,"reverseEndJoin":reverseJoin,"roundTrip":True}
    if t.RollBack()!=DB.TransactionStatus.RolledBack:
        raise ValueError("Rollback failed")
except:
    if t.GetStatus()==DB.TransactionStatus.Started:
        t.RollBack()
    raise
