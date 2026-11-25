# -*- coding: utf-8 -*-
"""Inspect dedicated link source and door-tag candidates without editing."""
from pyrevit.api import DB
import os
if doc is None or not doc.PathName.endswith("Smoke-9c3c99fd336746a0b555995ee2728a38.rvt"):
    raise ValueError("Wrong fixture")
p=os.path.join(os.path.dirname(doc.PathName),"LinkFixture-daec9dc194e945f8bfad51593a740b32.rvt")
t=DB.TransmissionData.ReadTransmissionData(DB.ModelPathUtils.ConvertUserVisiblePathToModelPath(p))
refs=[]
if t is not None:
    for i in t.GetAllExternalFileReferenceIds():
        r=t.GetLastSavedReferenceData(i)
        refs.append({"id":i.Value,"type":str(r.ExternalFileReferenceType),"status":str(r.GetLinkedFileStatus())})
v=doc.GetElement(DB.ElementId(312))
tags=[{"id":x.Id.Value,"name":x.FamilyName} for x in DB.FilteredElementCollector(doc).OfClass(DB.FamilySymbol).OfCategory(DB.BuiltInCategory.OST_DoorTags)]
doors=[x.Id.Value for x in DB.FilteredElementCollector(doc,v.Id).OfCategory(DB.BuiltInCategory.OST_Doors).WhereElementIsNotElementType()]
result={"refs":refs,"tagTypes":tags,"doors":doors[:20]}
