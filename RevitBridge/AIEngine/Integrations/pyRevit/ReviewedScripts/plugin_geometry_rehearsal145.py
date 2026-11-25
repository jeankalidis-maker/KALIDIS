# -*- coding: utf-8 -*-
"""Fixed, reviewed geometry/graphics rehearsal. Run only in pyRevit dry_run."""
from pyrevit.api import DB
if doc is None or doc.IsFamilyDocument or doc.IsWorkshared or doc.IsReadOnly or doc.PathName.lower() != r"C:\Users\naose\AppData\Local\KalidisAIEngine\Sandbox\Smoke-9c3c99fd336746a0b555995ee2728a38.rvt".lower():
    raise ValueError("Expected Smoke test project")
e=doc.GetElement(DB.ElementId(354362))
if e is None or e.UniqueId!="1037e414-fbfe-4843-b4e8-cadc8117fed1-0005683a" or e.Pinned or e.Host is not None or e.GroupId!=DB.ElementId.InvalidElementId:
    raise ValueError("Unexpected fixture")
view=doc.GetElement(DB.ElementId(326487))
if view is None or not view.AreGraphicsOverridesAllowed():
    raise ValueError("Unexpected 3D view")
from System.Collections.Generic import List
from System import Double
def xyz(x,y,z=0):
    return DB.XYZ(x/304.8,y/304.8,z/304.8)
def rgb(c):
    return [int(c.Red),int(c.Green),int(c.Blue)] if c.IsValid else None
t=DB.Transaction(doc,"Kalidis plugin API rehearsal")
t.Start()
try:
    ids=List[DB.ElementId]()
    origin=e.Location.Point
    for i in range(1,4):
        offset=xyz(0,3000*i)
        copies=list(DB.ElementTransformUtils.CopyElement(doc,e.Id,offset))
        doc.Regenerate()
        if len(copies)!=1:
            raise ValueError("Dependent copies")
        c=doc.GetElement(copies[0])
        if c.GetTypeId()!=e.GetTypeId() or c.Location.Point.DistanceTo(origin+offset)>.1/304.8:
            raise ValueError("Copy mismatch")
        ids.Add(c.Id)
    group=doc.Create.NewGroup(ids)
    doc.Regenerate()
    gid=group.Id
    if group.GetAvailableAttachedDetailGroupTypeIds().Count!=0:
        raise ValueError("Unexpected detail groups")
    member_ids=list(group.GetMemberIds())
    members=list(group.UngroupMembers())
    doc.Regenerate()
    if set([m.Value for m in members])!=set([m.Value for m in member_ids]):
        raise ValueError("Ungroup mismatch: previous={} returned={} group_exists={}".format([m.Value for m in member_ids],[m.Value for m in members],doc.GetElement(gid) is not None))
    if any(doc.GetElement(m).GroupId!=DB.ElementId.InvalidElementId for m in members):
        raise ValueError("Members still grouped")
    prior=view.GetElementOverrides(ids[0])
    ogs=view.GetElementOverrides(ids[0])
    ogs.SetProjectionLineColor(DB.Color(25,110,170))
    ogs.SetSurfaceTransparency(35)
    view.SetElementOverrides(ids[0],ogs)
    doc.Regenerate()
    check=view.GetElementOverrides(ids[0])
    if rgb(check.ProjectionLineColor)!=[25,110,170] or check.Transparency!=35:
        raise ValueError("Override mismatch")
    check.SetProjectionLineColor(DB.Color.InvalidColorValue)
    check.SetSurfaceTransparency(0)
    view.SetElementOverrides(ids[0],check)
    doc.Regenerate()
    check=view.GetElementOverrides(ids[0])
    if check.ProjectionLineColor.IsValid or check.Transparency!=0:
        raise ValueError("Clear override mismatch")
    plans=[v for v in DB.FilteredElementCollector(doc).OfClass(DB.ViewPlan) if not v.IsTemplate and v.ViewType==DB.ViewType.FloorPlan and v.GenLevel is not None and v.GenLevel.Id.Value==311]
    types=list(DB.FilteredElementCollector(doc).OfClass(DB.FilledRegionType))
    if not plans or not types:
        raise ValueError("Plan or filled region type missing")
    plan=plans[0]
    points=[DB.XYZ(x/304.8,y/304.8,plan.GenLevel.Elevation) for x,y in [(260000,205000),(262000,205000),(262000,206000),(260000,206000)]]
    loop=DB.CurveLoop()
    for i in range(4):
        loop.Append(DB.Line.CreateBound(points[i],points[(i+1)%4]))
    loops=List[DB.CurveLoop]()
    loops.Add(loop)
    region=DB.FilledRegion.Create(doc,types[0].Id,plan.Id,loops)
    doc.Regenerate()
    if region.OwnerViewId!=plan.Id or region.GetBoundaries().Count!=1:
        raise ValueError("Region mismatch")
    rid=region.Id.Value
    if t.Commit()!=DB.TransactionStatus.Committed:
        raise ValueError("Inner transaction failed")
    if doc.GetElement(gid) is not None:
        raise ValueError("Group exists after commit")
    result={"copy_ids":[i.Value for i in ids],"ungrouped":len(members),"graphics_set":True,"graphics_cleared":True,"region_id":rid,"plan_id":plan.Id.Value,"region_type_id":types[0].Id.Value}
except:
    if t.GetStatus()==DB.TransactionStatus.Started:
        t.RollBack()
    raise
