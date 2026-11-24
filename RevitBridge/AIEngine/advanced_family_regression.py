"""Live tests for horizontal relative position and native radius dimension.
Explicit sandbox-only use; each case preserves original type.
"""
from family_regression import create,rect,dimensions,check_dims
from reviewed_client import execute
from uuid import uuid4
from math import isclose,pi

def flex(e,p,i,parameters,expected,original,volumes):
    args=dict(id=i,typeName="Advanced-"+uuid4().hex,parameters=parameters,expectedDimensionsMm=expected,offsetMm=[3000,0,0])
    r=execute(e,"profile_flex",args,p);assert r["dryRun"]
    r=execute(e,"profile_flex",args,p,commit=True);assert r["status"]=="Committed"
    j=r["result"]["newId"];dims=dimensions(e,p,[i,j])
    check_dims(dims[i],original);check_dims(dims[j],expected)
    rows={x["id"]:x for x in execute(e,"solid_audit",{"ids":[i,j]},p)["result"]["elements"]}
    assert isclose(rows[i]["volumeM3"],volumes[0],abs_tol=1e-6) and isclose(rows[j]["volumeM3"],volumes[1],abs_tol=1e-6)
    return dict(originalId=i,newId=j,dimensionsMm=dims,volumes=rows)

def horizontal(e,p,l,t):
    spec=dict(shapes=[dict(zMm=0,heightMm=50,points=rect(0,0,600,400)),
       dict(zMm=500,heightMm=50,points=rect(0,0,600,400),widthParameter="Upper_Width",depthParameter="Upper_Depth",xParameter="Upper_X",yParameter="Upper_Y")])
    i=create(e,p,l,t,"XYRegression",spec,80000,26000)
    check_dims(dimensions(e,p,[i])[i],[600,400,550])
    return flex(e,p,i,dict(Upper_X=1300,Upper_Y=1200),[900,600,550],[600,400,550],[0.024,0.024])

def radial(e,p,l,t):
    spec=dict(shapes=[dict(zMm=0,heightMm=500,radiusMm=200,radiusParameter="Test_Radius",heightParameter="Test_Height")])
    i=create(e,p,l,t,"RadiusRegression",spec,90000,26000)
    check_dims(dimensions(e,p,[i])[i],[400,400,500])
    return flex(e,p,i,dict(Test_Radius=300,Test_Height=700),[600,600,700],[400,400,500],[pi*.2**2*.5,pi*.3**2*.7])
