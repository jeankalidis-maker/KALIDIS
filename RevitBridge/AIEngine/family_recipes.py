"""Reusable family specifications, in mm, for native RFA creation.
Offline validation here does not certify geometry or constraints in Revit.
"""
import math

def rectangle(x,y,w,l):
    if w<=0 or l<=0: raise ValueError("Positive dimensions required")
    return [{"x":a,"y":b} for a,b in [(x,y),(x+w,y),(x+w,y+l),(x,y+l)]]

def table_spec(width=1200,depth=600,height=750,leg=50,top=30):
    if not all(isinstance(v,(int,float)) and math.isfinite(v) and 1<=v<=30000 for v in (width,depth,height,leg,top)):
        raise ValueError("Invalid furniture dimensions")
    if min(width,depth)<=2*leg or height<=top: raise ValueError("Overlapping legs or invalid top")
    shapes=[dict(points=rectangle(0,0,width,depth),zMm=height-top,heightMm=top,heightParameter="Top_Thickness")]
    for x in (0,width-leg):
        for y in (0,depth-leg):
            shapes.append(dict(points=rectangle(x,y,leg,leg),zMm=0,heightMm=height-top,heightParameter="Leg_Height"))
    # Reference planes are layout guides, not geometry constraints.
    return dict(shapes=shapes,referencePlanes=[
        dict(name="Kalidis_Left",axis="x",offsetMm=0),
        dict(name="Kalidis_Right",axis="x",offsetMm=width),
        dict(name="Kalidis_Front",axis="y",offsetMm=0),
        dict(name="Kalidis_Back",axis="y",offsetMm=depth)],
        parameters=[dict(name="Nominal_Width",kind="length",value=width),dict(name="Nominal_Depth",kind="length",value=depth),dict(name="Description_Kalidis",kind="text",value="Table")])

def tapered_pedestal_spec():
    return dict(shapes=[dict(points=rectangle(-250,-250,500,500),zMm=0,heightMm=50)],
      blends=[dict(bottomMm=50,topMm=700,bottomPoints=rectangle(-200,-200,400,400),topPoints=rectangle(-100,-100,200,200)),
      dict(bottomMm=700,topMm=750,bottomPoints=rectangle(-250,-250,500,500),topPoints=rectangle(-300,-300,600,600))])

if __name__=="__main__":
    t=table_spec()
    assert len(t["shapes"])==5 and len(t["referencePlanes"])==4
    assert all(s["heightMm"]==720 for s in t["shapes"][1:])
    assert t["shapes"][0]["zMm"]==720
    assert len(tapered_pedestal_spec()["blends"])==2
    for bad in (-1,0,10):
        try:table_spec(width=bad)
        except ValueError:pass
        else:raise AssertionError("Invalid dimensions accepted")
    print("FAMILY_RECIPE_TESTS=PASS")
