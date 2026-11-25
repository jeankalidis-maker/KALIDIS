# -*- coding: utf-8 -*-
"""One explicit test instance; native pyRevit dry run must roll back."""
from pyrevit.api import DB
if doc is None or doc.IsFamilyDocument or doc.IsWorkshared or doc.IsReadOnly:
    raise ValueError("Expected local test project")
if doc.PathName.lower() != r"C:\Users\naose\AppData\Local\KalidisAIEngine\Sandbox\Smoke-9c3c99fd336746a0b555995ee2728a38.rvt".lower():
    raise ValueError("Wrong document")
e = doc.GetElement(DB.ElementId(354362))
if e is None or e.UniqueId != "1037e414-fbfe-4843-b4e8-cadc8117fed1-0005683a":
    raise ValueError("Wrong test element")
p = e.get_Parameter(DB.BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
if p is None or p.IsReadOnly or (p.AsString() or "") != "":
    raise ValueError("Unexpected existing value")
t = DB.Transaction(doc, "Kalidis agent dry run")
t.Start()
try:
    if not p.Set("KalidisAgentDryRun142"):
        raise ValueError("Parameter rejected")
    if t.Commit() != DB.TransactionStatus.Committed:
        raise ValueError("Inner transaction failed")
except:
    if t.GetStatus() == DB.TransactionStatus.Started:
        t.RollBack()
    raise
result = {"id": e.Id.Value, "inside_value": p.AsString()}
