# -*- coding: utf-8 -*-
"""Read-only audit of the active Revit document. No remote listener or model edits."""
from pyrevit import revit, DB, forms

doc = revit.doc
if doc is None:
    forms.alert("Abra um projeto Revit.")
else:
    walls = DB.FilteredElementCollector(doc).OfClass(DB.Wall).WhereElementIsNotElementType().GetElementCount()
    levels = DB.FilteredElementCollector(doc).OfClass(DB.Level).GetElementCount()
    families = DB.FilteredElementCollector(doc).OfClass(DB.Family).GetElementCount()
    forms.alert("Kalidis | Auditoria de leitura\nProjeto: {}\nParedes: {}\nNiveis: {}\nFamilias: {}".format(doc.Title, walls, levels, families))
