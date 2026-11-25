# -*- coding: utf-8 -*-
"""Enumerate full host-category scope with model-coordinate bounds. No writes."""
from pyrevit.api import DB
import os
import math
root=os.path.join(os.environ["LOCALAPPDATA"],"KalidisAIEngine","Sandbox")
if doc is None or os.path.normcase(os.path.dirname(doc.PathName))!=os.path.normcase(root) or doc.IsModified or doc.IsFamilyDocument or doc.IsWorkshared or doc.IsReadOnly or doc.IsModelInCloud:
    raise ValueError("Saved local Sandbox required")
if set(inputs.keys())!=set(["categories"]):
    raise ValueError("Unexpected arguments")
categories=inputs["categories"]
if not isinstance(categories,list) or not 1<=len(categories)<=40 or len(set(categories))!=len(categories):
    raise ValueError("Invalid categories")
# Explicit model categories, never views/spatial elements/links/rebar (separate structural audit).
supported=set(["OST_Walls","OST_Floors","OST_Ceilings","OST_Roofs","OST_Furniture","OST_Casework","OST_Doors","OST_Windows","OST_Columns","OST_StructuralColumns","OST_StructuralFoundation","OST_StructuralFraming","OST_PipeCurves","OST_PipeFitting","OST_PipeAccessory","OST_DuctCurves","OST_DuctFitting","OST_DuctAccessory","OST_Conduit","OST_ConduitFitting","OST_CableTray","OST_CableTrayFitting","OST_MechanicalEquipment","OST_ElectricalEquipment","OST_PlumbingFixtures","OST_GenericModel","OST_CurtainWallPanels","OST_CurtainWallMullions","OST_Stairs","OST_StairsRailing"])
if any(x not in supported for x in categories):
    raise ValueError("Unsupported category")
rows=[]
summary=[]
for name in categories:
    col=DB.FilteredElementCollector(doc).OfCategory(getattr(DB.BuiltInCategory,name)).WhereElementIsNotElementType()
    count=0
    for e in col:
        count+=1
        if len(rows)>=1000:
            raise ValueError("Scope exceeds 1000 instances; narrow categories")
        r={"id":e.Id.Value,"category":name,"minMm":None,"maxMm":None,"hostId":None}
        if isinstance(e,DB.FamilyInstance) and e.Host is not None:
            r["hostId"]=e.Host.Id.Value
        b=e.get_BoundingBox(None)
        if b is not None:
            corners=[b.Transform.OfPoint(DB.XYZ(x,y,z)) for x in [b.Min.X,b.Max.X] for y in [b.Min.Y,b.Max.Y] for z in [b.Min.Z,b.Max.Z]]
            v=[[p.X*304.8,p.Y*304.8,p.Z*304.8] for p in corners]
            r["minMm"]=[min(p[i] for p in v) for i in range(3)]
            r["maxMm"]=[max(p[i] for p in v) for i in range(3)]
        rows.append(r)
    summary.append({"category":name,"count":count})
result={"path":doc.PathName,"rows":rows,"categories":summary,"links":DB.FilteredElementCollector(doc).OfClass(DB.RevitLinkInstance).GetElementCount()}
