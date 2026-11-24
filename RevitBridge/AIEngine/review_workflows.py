"""Adapted project query and parameter update workflows from AndreyStartsev/revit-mcp.
See THIRD-PARTY-AndreyStartsev.txt. Uses Kalidis structured gateway, not in-process Python.
"""
from kalidis_client import Engine

def project_report(engine):
    snap=engine.snapshot()
    return dict(title=snap["title"],path=snap["path"],levels=snap["levels"],links=engine.call("get_linked_files")["links"],categories=engine.call("list_categories")["categories"])

def parameter_plan(ids,parameter_id,value):
    if not isinstance(ids,list) or not 1<=len(ids)<=100 or any(type(i) is not int or i<=0 for i in ids):
        raise ValueError("Explicit list of 1-100 element IDs required")
    if len(set(ids))!=len(ids):raise ValueError("Duplicate IDs")
    if type(parameter_id) is not int:raise ValueError("Parameter ID required")
    if type(value) is not str:raise ValueError("String parameter only")
    return [dict(action="set_parameter",elementId=i,parameterId=parameter_id,value=value) for i in ids]

def update_text(engine,expected_path,ids,parameter_id,value,commit=False):
    steps=parameter_plan(ids,parameter_id,value)
    snap=engine.snapshot()
    if snap["path"].casefold()!=str(expected_path).casefold():raise RuntimeError("Target document mismatch")
    before=engine.call("element_info",ids=ids)["elements"]
    def values(rows):
        result={}
        for row in rows:
            matches=[p for p in row["parameters"] if p["id"]==parameter_id]
            if len(matches)!=1 or matches[0]["storage"]!="String" or matches[0]["readOnly"]:
                raise ValueError("Missing or nonwritable string parameter")
            result[row["id"]]=matches[0]["value"]
        if set(result)!=set(ids):raise ValueError("Missing elements")
        return result
    initial=values(before)
    preview=engine.call("edit_elements",expectedPath=str(expected_path),steps=steps,commit=False)
    if values(engine.call("element_info",ids=ids)["elements"])!=initial:raise RuntimeError("Dry-run changed model")
    if not commit:return dict(ok=True,dryRun=True,preview=preview,before=initial)
    result=engine.call("edit_elements",expectedPath=str(expected_path),steps=steps,commit=True)
    after=values(engine.call("element_info",ids=ids)["elements"])
    return dict(ok=all(v==value for v in after.values()),committed=True,before=initial,after=after,result=result)

if __name__=="__main__":
    assert len(parameter_plan([1,2],-101,"Reviewed"))==2
    for ids in ([],[1,1],[0],["1"]):
        try:parameter_plan(ids,-101,"Reviewed")
        except ValueError:pass
        else:raise AssertionError("Invalid IDs accepted")
    class Mock:
        def __init__(self):self.value="Old"
        def snapshot(self):return {"path":"Test.rvt"}
        def call(self,name,**args):
            if name=="element_info":return {"elements":[{"id":1,"parameters":[{"id":-101,"storage":"String","readOnly":False,"value":self.value}]}]}
            if name=="edit_elements":
                if args["commit"]:self.value=args["steps"][0]["value"]
                return {"ok":True}
            raise AssertionError(name)
    m=Mock()
    assert update_text(m,"Test.rvt",[1],-101,"New")["dryRun"] and m.value=="Old"
    assert update_text(m,"Test.rvt",[1],-101,"New",True)["ok"] and m.value=="New"
    print("WORKFLOW_TESTS=PASS (mock only)")
