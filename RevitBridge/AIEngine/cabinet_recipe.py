"""Generate native cabinet RFA specifications from explicit measured dimensions.
Not image recognition. Width/depth are baked into profiles; thickness parameters
are attached only to individual extrusions where supported by the engine.
"""
from math import isfinite
from pathlib import Path

def cabinet_spec(width_mm, depth_mm, height_mm, thickness_mm=18, shelves=2, back_mm=6):
    w,d,h,t,b=map(float,(width_mm,depth_mm,height_mm,thickness_mm,back_mm))
    if not all(isfinite(x) for x in (w,d,h,t,b)):
        raise ValueError("Nonfinite dimension")
    if not (200<=w<=4000 and 150<=d<=1500 and 200<=h<=3500 and 5<=t<=50 and 3<=b<=30):
        raise ValueError("Cabinet dimensions outside supported range")
    if type(shelves) is not int or not 0<=shelves<=12:
        raise ValueError("0-12 shelves")
    if (h-(shelves+2)*t)/(shelves+1)<50 or w<=2*t or d<=b+t:
        raise ValueError("Insufficient interior clearance")
    def rect(x,y,z,dx,dy,dz):
        return dict(zMm=z,heightMm=dz,points=[dict(x=x,y=y),dict(x=x+dx,y=y),dict(x=x+dx,y=y+dy),dict(x=x,y=y+dy)])
    shapes=[rect(0,0,0,w,d,t),rect(0,0,h-t,w,d,t),
            rect(0,0,t,t,d,h-2*t),rect(w-t,0,t,t,d,h-2*t),
            rect(t,d-b,t,w-2*t,b,h-2*t)]
    gap=(h-(shelves+2)*t)/(shelves+1)
    for i in range(shelves):
        z=t+(i+1)*gap+i*t
        shapes.append(rect(t,0,z,w-2*t,d-b,t))
    parameters=[dict(name=k,kind="length",value=v) for k,v in
                [("Nominal_Width",w),("Nominal_Depth",d),("Nominal_Height",h),("Nominal_Board",t)]]
    return dict(shapes=shapes,parameters=parameters)

def expected_volume_m3(spec):
    def volume(s):
        p=s["points"];area=abs(sum(a["x"]*b["y"]-b["x"]*a["y"] for a,b in zip(p,p[1:]+p[:1])))/2
        return area*s["heightMm"]/1e9
    return sum(map(volume,spec["shapes"]))

if __name__=="__main__":
    s=cabinet_spec(1200,600,1800,18,3)
    assert len(s["shapes"])==8
    assert abs(expected_volume_m3(s)-0.11367864)<0.000000001,expected_volume_m3(s)
    for args in [(100,600,1800),(1200,600,200,50,12)]:
        try:cabinet_spec(*args)
        except ValueError:pass
        else:raise AssertionError("Bad geometry accepted")
    print("CABINET_RECIPE_TESTS=PASS")
