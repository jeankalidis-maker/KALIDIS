# -*- coding: utf-8 -*-
"""Reviewed bounded dry-run workflow for instance Comments and Mark."""
from pyrevit.api import DB
if doc is None or doc.IsFamilyDocument or doc.IsWorkshared or doc.IsReadOnly:
    raise ValueError("Expected local test project")
if doc.PathName.lower() != r"C:\Users\naose\AppData\Local\KalidisAIEngine\Sandbox\Smoke-9c3c99fd336746a0b555995ee2728a38.rvt".lower():
    raise ValueError("Wrong document")
if set(inputs.keys()) != set(["changes"]):
    raise ValueError("Unexpected inputs")
changes = inputs["changes"]
if not isinstance(changes, list) or not 1 <= len(changes) <= 20:
    raise ValueError("1-20 explicit edits required")
allowed = {"comments": DB.BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,
           "mark": DB.BuiltInParameter.ALL_MODEL_MARK}
seen = set()
pending = []
for c in changes:
    if set(c.keys()) != set(["id", "uniqueId", "parameter", "before", "after"]):
        raise ValueError("Unexpected fields")
    if c["parameter"] not in allowed or not isinstance(c["after"], type(u"")) or len(c["after"]) > 200:
        raise ValueError("Only bounded text parameters")
    key = (c["id"], c["parameter"])
    if key in seen:
        raise ValueError("Duplicate target")
    seen.add(key)
    e = doc.GetElement(DB.ElementId(int(c["id"])))
    if e is None or e.UniqueId != c["uniqueId"] or isinstance(e, DB.ElementType):
        raise ValueError("Instance identity mismatch")
    p = e.get_Parameter(allowed[c["parameter"]])
    if p is None or p.IsReadOnly or p.StorageType != DB.StorageType.String:
        raise ValueError("Unsupported parameter")
    if p.AsString() != c["before"] or c["after"] == c["before"]:
        raise ValueError("Stale or identical value")
    pending.append((c, p))
t = DB.Transaction(doc, "Kalidis reviewed parameter rehearsal")
t.Start()
try:
    for c, p in pending:
        if not p.Set(c["after"]):
            raise ValueError("Parameter update refused")
    doc.Regenerate()
    rows = [{"id": c["id"], "parameter": c["parameter"], "before": c["before"], "inside": p.AsString()} for c, p in pending]
    if any(row["inside"] != c["after"] for row, (c, p) in zip(rows, pending)):
        raise ValueError("Inside transaction readback mismatch")
    if t.Commit() != DB.TransactionStatus.Committed:
        raise ValueError("Inner transaction failed")
except:
    if t.GetStatus() == DB.TransactionStatus.Started:
        t.RollBack()
    raise
result = {"rows": rows, "path": doc.PathName}
