"""Opt-in fitting regression in a new, isolated MEP RVT.
Uses installed Autodesk templates; no external downloads or changes to original project.
"""
from kalidis_client import Engine
from reviewed_client import execute
from pathlib import Path
from uuid import uuid4
import argparse,json

def run(engine, template, discipline):
    original=Path(engine.snapshot()["path"])
    if original.parent.name!="Sandbox":raise RuntimeError("Open a sandbox project first")
    name="MEP-Regression-"+uuid4().hex+".rvt"
    engine.call("new_test_project",fileName=name,template=template)
    p=engine.call("open_test_project",fileName=name)["path"]
    if Path(p).name!=name:raise RuntimeError("Unexpected active project")
    results=[]
    try:
        types=engine.call("types")["types"]
        kind="PipeType" if discipline=="pipe" else "DuctType"
        systemKind="PipingSystemType" if discipline=="pipe" else "MechanicalSystemType"
        nameType="Standard" if discipline=="pipe" else "Rectangular Duct"
        print("TYPES",[(t["id"],t["name"],t["kind"]) for t in types if t["kind"] in (kind,systemKind)],flush=True)
        candidates=[t for t in types if t["kind"]==kind]
        if discipline=="duct":
            candidates=[t for t in candidates if "rectangular" in t["name"].lower()]
        t=next((x for x in candidates if x["name"]==nameType),candidates[0] if candidates else None)
        if t is None:raise RuntimeError("No compatible type")
        systemName="Domestic Cold Water" if discipline=="pipe" else "Supply Air"
        s=next(x for x in types if x["kind"]==systemKind and x["name"]==systemName)
        l=min(engine.snapshot()["levels"],key=lambda x:x["elevationMm"])["id"]
        def xyz(x,y,z=1500):return dict(x=x,y=y,z=z)
        def curve(a,b,alias,size=50):
            props=dict(action=discipline+"_sized",typeId=t["id"],systemTypeId=s["id"],levelId=l,start=a,end=b,alias=alias)
            if discipline=="pipe":props.update(diameterMm=size)
            else:props.update(widthMm=size*6,heightMm=200)
            return props
        cases=[
          ("elbow",[curve(xyz(0,0),xyz(3000,0),"a"),curve(xyz(3000,0),xyz(3000,3000),"b")],xyz(3000,0)),
          ("union",[curve(xyz(0,6000),xyz(3000,6000),"a"),curve(xyz(3000,6000),xyz(6000,6000),"b")],xyz(3000,6000)),
          ("transition",[curve(xyz(0,10000),xyz(3000,10000),"a",50),curve(xyz(3000,10000),xyz(6000,10000),"b",32)],xyz(3000,10000)),
          ("tee",[curve(xyz(0,14000),xyz(3000,14000),"a"),curve(xyz(3000,14000),xyz(6000,14000),"b"),curve(xyz(3000,14000),xyz(3000,17000),"c")],xyz(3000,14000))]
        for op,steps,point in cases:
            fit=dict(action=op,firstId="$a",secondId="$b",firstPoint=point,secondPoint=point)
            if op=="tee":fit.update(thirdId="$c",thirdPoint=point)
            try:
                dry=engine.batch(p,steps+[fit],commit=False)
                assert dry["verified"] and not dry["committed"]
                res=engine.batch(p,steps+[fit],commit=True)
                assert res["verified"] and res["committed"]
                topo=execute(engine,"mep_topology",{"ids":res["elementIds"]},p)["result"]
                assert topo["connectedEnds"]==(6 if op=="tee" else 4)
                row=dict(operation=op,passed=True,ids=res["elementIds"],connectedEnds=topo["connectedEnds"])
            except Exception as ex:
                row=dict(operation=op,passed=False,error=str(ex))
                results.append(row);print(json.dumps(row),flush=True)
                # Stop on timeout or unknown transaction status; never retry writes.
                if "timeout" in str(ex).lower() or "unknown" in str(ex).lower():raise
                continue
            results.append(row);print(json.dumps(row),flush=True)
        engine.call("save_test",expectedPath=p)
        return dict(path=p,results=results)
    finally:
        engine.call("open_test_project",fileName=original.name)

if __name__=="__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--template",required=True)
    parser.add_argument("--discipline",choices=["pipe","duct"],required=True)
    parser.add_argument("--create-test-project",action="store_true",required=True)
    args=parser.parse_args()
    print(json.dumps(run(Engine(read_only=False),args.template,args.discipline)),flush=True)
