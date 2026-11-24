"""Explicit opt-in family regression on the existing isolated test project.
Creates new uniquely named RFA files and instances; never changes the original type.
Run only with the desired sandbox RVT open. No automatic retry after timeout.
"""
from pathlib import Path
from uuid import uuid4
from math import isclose
import argparse, json
from kalidis_client import Engine
from reviewed_client import execute

def rect(x0,y0,x1,y1):
    return [dict(x=x,y=y) for x,y in [(x0,y0),(x1,y0),(x1,y1),(x0,y1)]]

def dimensions(engine,p,ids):
    r=execute(engine,"geometry_review",{"ids":ids},p)["result"]
    return {x["id"]:[b-a for a,b in zip(x["minMm"],x["maxMm"])] for x in r["elements"]}

def check_dims(actual,expected):
    if len(actual)!=3 or any(not isclose(a,b,abs_tol=0.2) for a,b in zip(actual,expected)):
        raise AssertionError(f"Dimensions: expected {expected}, got {actual}")

def create(engine,p,level,template,label,spec,x,y):
    f=engine.call("create_family_spec",expectedPath=p,template=template,fileName=label+"-"+uuid4().hex+".rfa",**spec)
    assert Path(f["path"]).is_file()
    fam=engine.call("load_family",expectedPath=p,path=f["path"],commit=True)
    types=engine.call("list_family_types",category="OST_Furniture")["familyTypes"]
    t=next(x for x in types if x["familyId"]==fam["id"])
    steps=[dict(action="component",levelId=level,typeId=t["id"],point=dict(x=x,y=y,z=0))]
    dry=engine.batch(p,steps,commit=False)
    assert dry["verified"] and not dry["committed"]
    r=engine.batch(p,steps,commit=True)
    assert r["verified"] and r["committed"]
    return r["elementIds"][0]

def run_void(engine,p,level,template):
    spec=dict(shapes=[dict(zMm=0,heightMm=500,points=rect(0,0,600,600)),
                      dict(zMm=-50,heightMm=600,points=rect(150,150,450,450),void=True)],
              voidCuts=[dict(solidIndex=0,voidIndex=1)])
    i=create(engine,p,level,template,"VoidRegression",spec,35000,22000)
    actual=execute(engine,"solid_audit",{"ids":[i]},p)["result"]["elements"][0]["volumeM3"]
    expected=(600*600-300*300)*500/1e9
    if not isclose(actual,expected,abs_tol=1e-6):
        raise AssertionError(f"Void volume expected {expected}, got {actual}; test instance {i}")
    check_dims(dimensions(engine,p,[i])[i],[600,600,500])
    return dict(id=i,volumeM3=actual,expectedM3=expected)

def run_profile(engine,p,level,template):
    spec=dict(shapes=[dict(zMm=0,heightMm=500,points=rect(0,0,600,400),widthParameter="Test_Width",depthParameter="Test_Depth",heightParameter="Test_Height")])
    i=create(engine,p,level,template,"ProfileRegression",spec,37000,22000)
    check_dims(dimensions(engine,p,[i])[i],[600,400,500])
    return dict(id=i,dimensionsMm=[600,400,500],note="Initial geometry only; run profile flex separately.")

if __name__=="__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--expected-path",required=True)
    parser.add_argument("--level",required=True,type=int)
    parser.add_argument("--template",required=True)
    parser.add_argument("--case",choices=["void","profile"],required=True)
    parser.add_argument("--commit-test-elements",action="store_true",required=True)
    args=parser.parse_args()
    e=Engine(read_only=False);p=e.snapshot()["path"]
    if Path(p)!=Path(args.expected_path) or Path(p).parent.name!="Sandbox":
        raise RuntimeError("Wrong active project")
    result=(run_void if args.case=="void" else run_profile)(e,p,args.level,args.template)
    print(json.dumps(result),flush=True)
    print(json.dumps(e.call("save_test",expectedPath=p)),flush=True)
