"""Regression for native formula-controlled relative vertical placement.
Requires engine with extrusion baseParameter/endParameter support.
"""
from family_regression import create,rect,dimensions,check_dims
from reviewed_client import execute
from uuid import uuid4
from math import isclose

def run(e,p,level,template):
    spec=dict(shapes=[
      dict(zMm=0,heightMm=50,points=rect(0,0,600,400),heightParameter="Bottom_Thickness"),
      dict(zMm=500,heightMm=50,points=rect(0,0,600,400),baseParameter="Top_Base",endParameter="Top_End")],
      parameters=[dict(name="Gap_Height",kind="length",value=450)],
      formulas={"Top_Base":"Bottom_Thickness + Gap_Height","Top_End":"Top_Base + Bottom_Thickness"})
    i=create(e,p,level,template,"RelativeFlex",spec,75000,26000)
    check_dims(dimensions(e,p,[i])[i],[600,400,550])
    args=dict(id=i,typeName="Relative-"+uuid4().hex,parameters=dict(Gap_Height=750),expectedDimensionsMm=[600,400,850],offsetMm=[2000,0,0])
    dry=execute(e,"profile_flex",args,p);assert dry["dryRun"]
    b=execute(e,"profile_flex",args,p,commit=True);assert b["status"]=="Committed"
    j=b["result"]["newId"];dims=dimensions(e,p,[i,j])
    check_dims(dims[i],[600,400,550]);check_dims(dims[j],[600,400,850])
    solids=execute(e,"solid_audit",{"ids":[i,j]},p)["result"]["elements"]
    assert all(x["solidCount"]==2 and isclose(x["volumeM3"],0.024,abs_tol=1e-6) for x in solids)
    return dict(originalId=i,newId=j,dimensionsMm=dims,solids=solids)
