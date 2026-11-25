from pyrevit.api import DB
if doc is None or not doc.PathName.endswith("Smoke-9c3c99fd336746a0b555995ee2728a38.rvt"):
    raise ValueError("Wrong fixture")
rows=[]
for i in [354362,349042,358096,358097]:
    e=doc.GetElement(DB.ElementId(i))
    if not isinstance(e, DB.FamilyInstance):
        raise ValueError("Unexpected fixture")
    m=e.MEPModel
    manager=m.ConnectorManager if m is not None else None
    rows.append({"id":i,"mepModel":m is not None,"manager":manager is not None,"connectorCount":manager.Connectors.Size if manager is not None else 0,"category":e.Category.Name})
result={"rows":rows}
