# -*- coding: utf-8 -*-
"""Bounded host versus linked native solid intersection. Never edits a document."""
from pyrevit.api import DB
import Autodesk.Revit.Exceptions
import os
import math
root=os.path.join(os.environ["LOCALAPPDATA"],"KalidisAIEngine","Sandbox")
if doc is None or os.path.normcase(os.path.dirname(doc.PathName))!=os.path.normcase(root) or doc.IsModified or doc.IsFamilyDocument or doc.IsWorkshared or doc.IsReadOnly or doc.IsModelInCloud:
    raise ValueError("Saved local fixture required")
if set(inputs.keys())!=set(["pairs","toleranceM3"]):
    raise ValueError("Unexpected inputs")
pairs=inputs["pairs"]
tol=inputs["toleranceM3"]
if not isinstance(pairs,list) or not 1<=len(pairs)<=20 or not 0<=tol<=1:
    raise ValueError("Scope limit")
opt=DB.Options()
opt.DetailLevel=DB.ViewDetailLevel.Fine
opt.IncludeNonVisibleObjects=False
def get(d,i,u):
    e=d.GetElement(DB.ElementId(int(i)))
    if e is None or e.UniqueId!=u or isinstance(e,DB.ElementType) or isinstance(e,DB.SpatialElement) or isinstance(e,DB.RevitLinkInstance):
        raise ValueError("Element identity or type mismatch")
    return e
def solids(e,tr):
    out=[]
    def walk(g,depth):
        if depth>8:
            raise ValueError("Geometry depth limit")
        if g is None: return
        for v in g:
            if isinstance(v,DB.GeometryInstance):
                walk(v.GetInstanceGeometry(),depth+1)
            elif isinstance(v,DB.Solid) and v.Faces.Size>0 and v.Volume>1e-9:
                if len(out)>=100: raise ValueError("Solid limit")
                out.append(DB.SolidUtils.CreateTransformed(v,tr) if tr is not None else v)
    walk(e.get_Geometry(opt),0)
    return out
def xyz(p,f=1):
    return [p.X*f,p.Y*f,p.Z*f]
def location(d):
    loc=d.ActiveProjectLocation
    p=loc.GetProjectPosition(DB.XYZ.Zero)
    return {"name":loc.Name,"eastWestMm":p.EastWest*304.8,"northSouthMm":p.NorthSouth*304.8,"elevationMm":p.Elevation*304.8,"angleRad":p.Angle}
def transform(t):
    return {"originMm":xyz(t.Origin,304.8),"basisX":xyz(t.BasisX),"basisY":xyz(t.BasisY),"basisZ":xyz(t.BasisZ),"determinant":t.Determinant}
rows=[]
seen=set()
for p in pairs:
    if set(p.keys())!=set(["hostId","hostUniqueId","linkId","linkUniqueId","linkedId","linkedUniqueId"]):
        raise ValueError("Invalid pair")
    k=(p["hostId"],p["linkId"],p["linkedId"])
    if k in seen: raise ValueError("Duplicate pair")
    seen.add(k)
    link=doc.GetElement(DB.ElementId(int(p["linkId"])))
    if not isinstance(link,DB.RevitLinkInstance) or link.UniqueId!=p["linkUniqueId"]:
        raise ValueError("Link identity mismatch")
    ld=link.GetLinkDocument()
    if ld is None or os.path.normcase(os.path.dirname(ld.PathName))!=os.path.normcase(root) or not os.path.basename(ld.PathName).startswith("LinkFixture-") or ld.IsModified or ld.IsWorkshared:
        raise ValueError("Loaded saved local fixture link required")
    t=link.GetTotalTransform()
    if not t.IsConformal or t.Determinant<=0 or abs(t.Scale-1)>1e-9:
        raise ValueError("Rigid non-reflected transform required")
    a=get(doc,p["hostId"],p["hostUniqueId"])
    b=get(ld,p["linkedId"],p["linkedUniqueId"])
    x,y=solids(a,None),solids(b,t)
    if len(x)*len(y)>400: raise ValueError("Solid combinations limit")
    maximum,total,tests,failed=0.0,0.0,0,0
    for s in x:
        for q in y:
            tests+=1
            try:
                inter=DB.BooleanOperationsUtils.ExecuteBooleanOperation(s,q,DB.BooleanOperationsType.Intersect)
                v=abs(inter.Volume)*0.028316846592
                maximum=max(maximum,v)
                total+=v
                inter.Dispose()
            except Autodesk.Revit.Exceptions.InvalidOperationException:
                failed+=1
    status="overlap" if maximum>tol else "inconclusive" if failed or tests==0 else "no_overlap_above_tolerance"
    rows.append(dict(p,hostSolidCount=len(x),linkedSolidCount=len(y),tests=tests,failed=failed,maxPairOverlapM3=maximum,sumPairOverlapM3=total,status=status,totalTransform=transform(t),instanceTransform=transform(link.GetTransform()),hostProjectLocation=location(doc),linkedProjectLocation=location(ld)))
result={"path":doc.PathName,"toleranceM3":tol,"rows":rows}
