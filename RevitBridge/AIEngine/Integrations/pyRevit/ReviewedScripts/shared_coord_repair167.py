# -*- coding: utf-8 -*-
"""Fixed Sandbox-only shared coordinate acquisition and repair rehearsal; rollback always."""
from pyrevit.api import DB
import os
import hashlib
import math
root=os.path.join(os.environ["LOCALAPPDATA"],"KalidisAIEngine","Sandbox")
host=os.path.join(root,"Smoke-9c3c99fd336746a0b555995ee2728a38.rvt")
source=os.path.join(root,"LinkFixture-d21eb931eb83427fa31f9115f7a3e3f7.rvt")
if doc is None or doc.PathName.lower()!=host.lower() or doc.IsModified or doc.IsWorkshared or doc.IsReadOnly or doc.IsFamilyDocument:
    raise ValueError("Saved exact Smoke fixture required")
def el(i,u):
    e=doc.GetElement(DB.ElementId(i))
    if e is None or e.UniqueId!=u: raise ValueError("Identity mismatch")
    return e
link=el(354377,"4a94877a-d841-43a1-ba89-9d66622c1257-00056849")
duplicate=el(340286,"9f71d027-7097-4fff-8fb8-2e087c082750-0005313e")
target=el(354379,"4a94877a-d841-43a1-ba89-9d66622c1257-0005684b")
ld=link.GetLinkDocument()
if ld is None or ld.PathName.lower()!=source.lower() or ld.IsModified or duplicate.GetLinkDocument().PathName!=ld.PathName:
    raise ValueError("Link mismatch")
wall=ld.GetElement(DB.ElementId(325322))
if wall is None or wall.UniqueId!="80c64bae-8410-458b-a330-9769f4ed36ab-0004f6ca" or not isinstance(wall,DB.Wall):
    raise ValueError("Linked wall mismatch")
if target.Host is not None or target.Pinned or target.GroupId!=DB.ElementId.InvalidElementId or not isinstance(target.Location,DB.LocationPoint):
    raise ValueError("Target scope")
def digest(p):
    h=hashlib.sha256()
    with open(p,'rb') as stream:
        while True:
            block=stream.read(1048576)
            if not block: break
            h.update(block)
    return h.hexdigest()
def coord(d,p):
    v=d.ActiveProjectLocation.GetProjectPosition(p)
    return DB.XYZ(v.EastWest,v.NorthSouth,v.Elevation)
def vec(p): return [p.X*304.8,p.Y*304.8,p.Z*304.8]
def close(a,b): return a.DistanceTo(b)*304.8<.1
def solids(e,tr=None):
    opt=DB.Options()
    opt.DetailLevel=DB.ViewDetailLevel.Fine
    out=[]
    def walk(g,depth):
        if depth>8: raise ValueError("Depth limit")
        if g is None: return
        for o in g:
            if isinstance(o,DB.GeometryInstance): walk(o.GetInstanceGeometry(),depth+1)
            elif isinstance(o,DB.Solid) and o.Faces.Size>0 and o.Volume>1e-9:
                if len(out)>=20: raise ValueError("Solid limit")
                out.append(DB.SolidUtils.CreateTransformed(o,tr) if tr is not None else o)
    walk(e.get_Geometry(opt),0)
    return out
def overlap(e):
    x,y=solids(e),solids(wall,link.GetTotalTransform())
    if not x or not y: raise ValueError("Missing solid")
    v=0.0
    for a in x:
        for b in y:
            q=DB.BooleanOperationsUtils.ExecuteBooleanOperation(a,b,DB.BooleanOperationsType.Intersect)
            v+=abs(q.Volume)*0.028316846592
            q.Dispose()
    return v
def bounds(e,tr=None):
    b=e.get_BoundingBox(None)
    if b is None: raise ValueError("No bounds")
    pts=[b.Transform.OfPoint(DB.XYZ(x,y,z)) for x in [b.Min.X,b.Max.X] for y in [b.Min.Y,b.Max.Y] for z in [b.Min.Z,b.Max.Z]]
    if tr is not None: pts=[tr.OfPoint(p) for p in pts]
    v=[[p.X,p.Y,p.Z] for p in pts]
    return ([min(p[i] for p in v) for i in range(3)],[max(p[i] for p in v) for i in range(3)])
def gap(a,b):
    return math.sqrt(sum(max(0,a[0][i]-b[1][i],b[0][i]-a[1][i])**2 for i in range(3)))*304.8
before_hash=digest(source)
before_origin=coord(doc,DB.XYZ.Zero)
before_tr=link.GetTotalTransform()
before_target=target.Location.Point
if before_origin.GetLength()>1e-6 or not close(before_target,DB.XYZ(758699.8/304.8,94600/304.8,0)):
    raise ValueError("Unexpected baseline")
t=DB.Transaction(doc,"Kalidis shared-coordinate repair rehearsal")
t.Start()
try:
    # AcquireCoordinates forbids multiple placements; remove only the exact duplicate
    # within a transaction which is ALWAYS rolled back. Reject dependent deletion.
    deleted=list(doc.Delete(duplicate.Id))
    if len(deleted)!=1 or deleted[0].Value!=340286:
        raise ValueError("Dependent deletion refused")
    doc.Regenerate()
    doc.AcquireCoordinates(link.Id)
    doc.Regenerate()
    after_origin=coord(doc,DB.XYZ.Zero)
    tr=link.GetTotalTransform()
    if after_origin.GetLength()*304.8<1000 or not close(tr.Origin,before_tr.Origin) or not close(tr.BasisX,before_tr.BasisX) or not close(tr.BasisY,before_tr.BasisY) or not close(target.Location.Point,before_target):
        raise ValueError("Internal coordinates changed or shared origin not acquired")
    probes=[DB.XYZ.Zero,DB.XYZ(10,0,0),DB.XYZ(0,10,0),DB.XYZ(3,7,2)]
    residuals=[coord(doc,tr.OfPoint(p)).DistanceTo(coord(ld,p))*304.8 for p in probes]
    if max(residuals)>.1:
        raise ValueError("Shared coordinate mismatch {}".format(residuals))
    copies=list(DB.ElementTransformUtils.CopyElement(doc,target.Id,DB.XYZ(800.2/304.8,0,0)))
    if len(copies)!=1: raise ValueError("Dependent copy")
    e=doc.GetElement(copies[0])
    doc.Regenerate()
    start=e.Location.Point
    linked_bounds=bounds(wall,tr)
    a=overlap(e)
    if abs(a-.036)>1e-7 or gap(bounds(e),linked_bounds)>1e-6:
        raise ValueError("Expected overlap missing")
    # Explicit conservative move, equivalent to the previously verified fixture repair.
    delta=DB.XYZ(-800.2/304.8,0,0)
    # Reject other nearby local obstacle boxes except the deliberate original fixture.
    # The copy would coincide with the original if moved left; instead move the
    # original temporarily within the same rollback before the repair, checking scope.
    DB.ElementTransformUtils.MoveElement(doc,target.Id,DB.XYZ(0,-5000/304.8,0))
    doc.Regenerate()
    DB.ElementTransformUtils.MoveElement(doc,e.Id,delta)
    doc.Regenerate()
    b=overlap(e)
    g=gap(bounds(e),linked_bounds)
    if b>1e-7 or g<300 or not close(e.Location.Point,start+delta):
        raise ValueError("Repair failed")
    # Check nearby obstacles for the final furniture clearance, not a complete
    # building-wide circulation check.
    bb=bounds(e)
    c=300.0/304.8
    outline=DB.Outline(DB.XYZ(*[x-c for x in bb[0]]),DB.XYZ(*[x+c for x in bb[1]]))
    from System.Collections.Generic import List
    cats=List[DB.BuiltInCategory]()
    for k in [DB.BuiltInCategory.OST_Walls,DB.BuiltInCategory.OST_Furniture,DB.BuiltInCategory.OST_StructuralColumns,DB.BuiltInCategory.OST_Columns,DB.BuiltInCategory.OST_Casework,DB.BuiltInCategory.OST_Doors,DB.BuiltInCategory.OST_Windows]:
        cats.Add(k)
    other=[]
    for o in DB.FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(DB.ElementMulticategoryFilter(cats)).WherePasses(DB.BoundingBoxIntersectsFilter(outline)):
        if o.Id!=e.Id and gap(bb,bounds(o))<300-.01:
            other.append(o.Id.Value)
    if other: raise ValueError("Local obstacle after repair {}".format(other))
    result={"sharedOriginMm":vec(after_origin),"residualsMm":residuals,"overlapBeforeM3":a,"overlapAfterM3":b,"gapAfterMm":g,"copyId":e.Id.Value,"deltaMm":vec(delta),"internalCoordinatesPreservedOnAcquire":True}
    if t.RollBack()!=DB.TransactionStatus.RolledBack: raise ValueError("Rollback failure")
except:
    if t.GetStatus()==DB.TransactionStatus.Started: t.RollBack()
    raise
if doc.GetElement(DB.ElementId(result["copyId"])) is not None or doc.GetElement(DB.ElementId(340286)) is None or not close(target.Location.Point,before_target) or not close(coord(doc,DB.XYZ.Zero),before_origin) or digest(source)!=before_hash:
    raise ValueError("Post-rollback mismatch")
result["restored"]=True
result["linkedHashPreserved"]=True
