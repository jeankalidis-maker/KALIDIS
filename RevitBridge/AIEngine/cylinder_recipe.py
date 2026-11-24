"""Create new native circular RFA variants; does not claim radial type flex."""
from family_regression import create
from reviewed_client import execute
from math import pi,isclose
def generate(e,p,level,template,radius,height,x,y):
    if not 10<=radius<=10000 or not 1<=height<=30000:raise ValueError("Invalid size")
    spec=dict(shapes=[dict(zMm=0,heightMm=height,radiusMm=radius,heightParameter="Test_Height")])
    i=create(e,p,level,template,"CylinderRecipe",spec,x,y)
    v=execute(e,"cylinder_flex",dict(id=i,mode="audit"),p)["result"]
    assert isclose(v["radiusMm"],radius,abs_tol=.01) and isclose(v["heightMm"],height,abs_tol=.01)
    assert isclose(v["volumeM3"],pi*(radius/1000)**2*height/1000,rel_tol=1e-4)
    return v
