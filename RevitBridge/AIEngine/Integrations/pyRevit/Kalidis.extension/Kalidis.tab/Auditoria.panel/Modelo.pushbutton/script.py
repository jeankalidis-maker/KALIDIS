# -*- coding: utf-8 -*-
"""Read-only audit. Same inventory function for ribbon and pyRevit agent."""
from pyrevit import revit, DB, forms

def audit_document(document):
    if document is None:
        raise ValueError("Abra um projeto Revit.")
    return {
        "title": document.Title,
        "path": document.PathName,
        "walls": DB.FilteredElementCollector(document).OfClass(DB.Wall).WhereElementIsNotElementType().GetElementCount(),
        "levels": DB.FilteredElementCollector(document).OfClass(DB.Level).GetElementCount(),
        "families": DB.FilteredElementCollector(document).OfClass(DB.Family).GetElementCount(),
        "materials": DB.FilteredElementCollector(document).OfClass(DB.Material).GetElementCount(),
        "sheets": DB.FilteredElementCollector(document).OfClass(DB.ViewSheet).GetElementCount()
    }

# The agent can execute this exact registered extension file noninteractively.
# Ordinary ribbon execution still displays the audit dialog.
if globals().get("inputs", {}).get("auditOnly", False) is True:
    if set(inputs.keys()) != set(["auditOnly", "expectedPath"]):
        raise ValueError("Unexpected inputs")
    if revit.doc is None or revit.doc.PathName != inputs["expectedPath"] or revit.doc.IsModified:
        raise ValueError("Expected saved audit document")
    result = audit_document(revit.doc)
else:
    if revit.doc is None:
        forms.alert("Abra um projeto Revit.")
    else:
        data = audit_document(revit.doc)
        forms.alert("Kalidis | Auditoria de leitura\nProjeto: {}\nParedes: {}\nNiveis: {}\nFamilias: {}\nMateriais: {}\nFolhas: {}".format(data["title"], data["walls"], data["levels"], data["families"], data["materials"], data["sheets"]))
