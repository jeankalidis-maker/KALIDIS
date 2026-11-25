# -*- coding: utf-8 -*-
"""Create transient Revit geometry from explicit line/arc loops. No document edits."""
from pyrevit.api import DB
from System.Collections.Generic import List
import os
import math
root=os.path.join(os.environ["LOCALAPPDATA"],"KalidisAIEngine","Sandbox")
if doc is None or os.path.normcase(os.path.dirname(doc.PathName))!=os.path.normcase(root) or doc.IsModified or doc.IsFamilyDocument or doc.IsWorkshared or doc.IsReadOnly or doc.IsModelInCloud:
    raise ValueError("Saved local Sandbox required")
if set(inputs.keys())!=set(["loops"]):
    raise ValueError("Unexpected arguments")
loops=inputs["loops"]
if not isinstance(loops,list) or not 1<=len(loops)<=8 or sum(len(x) for x in loops)>100:
    raise ValueError("Profile limits")
def pt(p):
    if len(p)!=3 or any(isinstance(x,bool) or not isinstance(x,(int,float)) or math.isnan(x) or math.isinf(x) or abs(x)>1000000 for x in p):
        raise ValueError("Invalid point")
    return DB.XYZ(*[float(x)/304.8 for x in p])
def make_loop(items):
    loop=DB.CurveLoop()
    if not 2<=len(items)<=100:
        raise ValueError("Invalid loop size")
    for s in items:
        a,b=pt(s["start"]),pt(s["end"])
        if s["kind"]=="line" and set(s.keys())==set(["kind","start","end"]):
            curve=DB.Line.CreateBound(a,b)
        elif s["kind"]=="arc" and set(s.keys())==set(["kind","start","end","mid"]):
            curve=DB.Arc.Create(a,b,pt(s["mid"]))
        else:
            raise ValueError("Unsupported curve")
        if curve.Length<=app.ShortCurveTolerance:
            raise ValueError("Short curve")
        loop.Append(curve)
    if loop.IsOpen() or not loop.HasPlane():
        raise ValueError("Open or nonplanar loop")
    return loop
try:
    native=[make_loop(x) for x in loops]
    plane=native[0].GetPlane()
    direction=plane.Normal.Normalize()
    for l in native:
        p=l.GetPlane()
        if abs(direction.DotProduct(p.Normal.Normalize()))<1-1e-9 or abs(direction.DotProduct(p.Origin-plane.Origin))>1e-7:
            raise ValueError("Loops not coplanar")
    def extrusion(ls):
        v=List[DB.CurveLoop]()
        for l in ls: v.Add(l)
        return DB.GeometryCreationUtilities.CreateExtrusionGeometry(v,direction,1.0)
    solids=[extrusion([l]) for l in native]
    for i in range(1,len(solids)):
        hole=solids[i]
        cut=DB.BooleanOperationsUtils.ExecuteBooleanOperation(solids[0],hole,DB.BooleanOperationsType.Intersect)
        if hole.Volume<=0 or abs(cut.Volume-hole.Volume)>max(1e-8,hole.Volume*1e-5):
            raise ValueError("Hole outside outer boundary: index={} hole={} intersection={}".format(i,hole.Volume,cut.Volume))
        for j in range(1,i):
            overlap=DB.BooleanOperationsUtils.ExecuteBooleanOperation(solids[j],hole,DB.BooleanOperationsType.Intersect)
            if overlap.Volume>1e-8:
                raise ValueError("Intersecting or nested holes")
    final=extrusion(native)
    # Native combined extrusion enforces disjoint coplanar loops. Volume checks are approximate for curved faces.
    expected=solids[0].Volume-sum(x.Volume for x in solids[1:])
    if expected<=0 or abs(final.Volume-expected)>max(1e-8,expected*1e-5):
        raise ValueError("Net area mismatch")
    result={"path":doc.PathName,"valid":True,"normal":[direction.X,direction.Y,direction.Z],"loopAreasMm2":[s.Volume*304.8**2 for s in solids],"netAreaMm2":final.Volume*304.8**2,"perimetersMm":[sum(c.Length for c in l)*304.8 for l in native],"curveCounts":[len(x) for x in loops]}
except Exception as ex:
    result={"path":doc.PathName,"valid":False,"reason":str(ex)[:500]}
