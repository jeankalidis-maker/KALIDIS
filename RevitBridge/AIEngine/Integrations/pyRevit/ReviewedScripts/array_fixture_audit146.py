from pyrevit.api import DB
if doc is None or not doc.PathName.endswith("Smoke-9c3c99fd336746a0b555995ee2728a38.rvt"):
    raise ValueError("Wrong fixture")
rows=[]
for e in DB.FilteredElementCollector(doc).OfClass(DB.FamilyInstance).WhereElementIsNotElementType():
    if e.Id.Value in [354362,358096,358097,358098,349042,359715,360558] or (e.Host is None and e.MEPModel is None and isinstance(e.Location, DB.LocationPoint) and not e.Pinned and e.GroupId==DB.ElementId.InvalidElementId and len(rows)<25):
        rows.append({"id":e.Id.Value,"uniqueId":e.UniqueId,"category":e.Category.Name if e.Category else None,"host":e.Host.Id.Value if e.Host else None,"mep":e.MEPModel is not None,"point":isinstance(e.Location,DB.LocationPoint),"pinned":e.Pinned,"group":e.GroupId.Value,"comment":e.get_Parameter(DB.BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString()})
result={"rows":rows}
