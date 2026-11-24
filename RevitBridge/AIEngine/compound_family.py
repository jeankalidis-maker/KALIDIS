"""Compound native rectangular family recipe and live regression.
Two disjoint solid slabs share width/depth through native formulas and thickness
through an associated length parameter. Fixed vertical separation, not a full cabinet.
"""
from family_regression import create,rect,dimensions,check_dims
from reviewed_client import execute
from uuid import uuid4
from math import isclose

def spec():
    return dict(shapes=[
        dict(zMm=0,heightMm=50,points=rect(0,0,600,400),widthParameter="Master_Width",depthParameter="Master_Depth",heightParameter="Plate_Thickness"),
        dict(zMm=500,heightMm=50,points=rect(0,0,600,400),widthParameter="Upper_Width",depthParameter="Upper_Depth",heightParameter="Plate_Thickness")],
        formulas={"Upper_Width":"Master_Width","Upper_Depth":"Master_Depth"})

def run(e,p,level,template):
    i=create(e,p,level,template,"CompoundFlex",spec(),70000,22000)
    check_dims(dimensions(e,p,[i])[i],[600,400,550])
    a=dict(id=i,typeName="Compound-"+uuid4().hex,parameters=dict(Master_Width=800,Master_Depth=550,Plate_Thickness=80),expectedDimensionsMm=[800,550,580],offsetMm=[2000,0,0])
    dry=execute(e,"profile_flex",a,p);assert dry["dryRun"]
    b=execute(e,"profile_flex",a,p,commit=True);assert b["status"]=="Committed"
    j=b["result"]["newId"];dims=dimensions(e,p,[i,j])
    check_dims(dims[i],[600,400,550]);check_dims(dims[j],[800,550,580])
    solids=execute(e,"solid_audit",{"ids":[i,j]},p)["result"]["elements"]
    rows={x["id"]:x for x in solids}
    assert rows[i]["solidCount"]==2 and rows[j]["solidCount"]==2
    assert isclose(rows[i]["volumeM3"],0.024,abs_tol=1e-6)
    assert isclose(rows[j]["volumeM3"],0.0704,abs_tol=1e-6)
    return dict(originalId=i,newId=j,dimensionsMm=dims,volumes=solids)
