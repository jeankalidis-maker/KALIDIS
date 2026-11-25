# -*- coding: utf-8 -*-
"""Read explicit native material appearance bitmap references; no file access."""
from pyrevit.api import DB
from Autodesk.Revit.DB.Visual import Asset, AssetPropertyString, UnifiedBitmap
import os
import re
root=os.path.join(os.environ["LOCALAPPDATA"],"KalidisAIEngine","Sandbox")
if doc is None or os.path.normcase(os.path.dirname(doc.PathName))!=os.path.normcase(root) or doc.IsModified or doc.IsFamilyDocument or doc.IsWorkshared or doc.IsReadOnly or doc.IsModelInCloud:
    raise ValueError("Saved local fixture required")
if set(inputs.keys())!=set(["items"]):
    raise ValueError("Unexpected inputs")
items=inputs["items"]
if not isinstance(items,list) or not 1<=len(items)<=100:
    raise ValueError("Explicit material scope required")
seen=set()
rows=[]
for item in items:
    if set(item.keys())!=set(["id","uniqueId"]) or item["id"] in seen:
        raise ValueError("Duplicate or invalid identity")
    seen.add(item["id"])
    material=doc.GetElement(DB.ElementId(int(item["id"])))
    if not isinstance(material,DB.Material) or material.UniqueId!=item["uniqueId"]:
        raise ValueError("Material identity mismatch")
    maps=[]
    visited=[0]
    def walk(prop,depth):
        visited[0]+=1
        if depth>12 or visited[0]>3000:
            raise ValueError("Appearance graph limit")
        if isinstance(prop,AssetPropertyString) and prop.Name==UnifiedBitmap.UnifiedbitmapBitmap:
            value=prop.Value or ""
            state="unresolved_path"
            name=None
            if os.path.isabs(value) and os.path.normcase(os.path.dirname(value))==os.path.normcase(root) and re.match(r"^TextureFixture-[a-f0-9]{32}\.png$",os.path.basename(value)):
                state="staged"
                name=os.path.basename(value)
            elif not value.strip():
                state="empty"
            maps.append({"state":state,"fileName":name})
            if len(maps)>30:
                raise ValueError("Too many maps")
        if isinstance(prop,Asset):
            for i in range(prop.Size):
                walk(prop[i],depth+1)
        for j in range(prop.NumberOfConnectedProperties):
            walk(prop.GetConnectedProperty(j),depth+1)
    a=doc.GetElement(material.AppearanceAssetId)
    if isinstance(a,DB.AppearanceAssetElement):
        walk(a.GetRenderingAsset(),0)
    rows.append({"id":material.Id.Value,"uniqueId":material.UniqueId,"assetId":material.AppearanceAssetId.Value,"maps":maps})
result={"path":doc.PathName,"rows":rows}
