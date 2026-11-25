# -*- coding: utf-8 -*-
"""Kalidis fixed audit; no transactions, file, process or network operations."""
from pyrevit.api import DB

if doc is None or doc.IsFamilyDocument or doc.IsWorkshared or doc.IsReadOnly:
    raise ValueError("Expected local experimental project")
if doc.PathName.lower() != r"C:\Users\naose\AppData\Local\KalidisAIEngine\Sandbox\Smoke-9c3c99fd336746a0b555995ee2728a38.rvt".lower():
    raise ValueError("Unexpected active project")
result = {
    "path": doc.PathName,
    "viewId": uidoc.ActiveView.Id.Value,
    "walls": DB.FilteredElementCollector(doc).OfClass(DB.Wall).WhereElementIsNotElementType().GetElementCount(),
    "levels": DB.FilteredElementCollector(doc).OfClass(DB.Level).GetElementCount(),
    "families": DB.FilteredElementCollector(doc).OfClass(DB.Family).GetElementCount(),
    "materials": DB.FilteredElementCollector(doc).OfClass(DB.Material).GetElementCount(),
    "sheets": DB.FilteredElementCollector(doc).OfClass(DB.ViewSheet).GetElementCount(),
    "modified": doc.IsModified,
    "selectionCount": uidoc.Selection.GetElementIds().Count
}
