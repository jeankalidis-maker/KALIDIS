"""Deterministic reusable architectural recipes for Kalidis.
All lengths are millimeters. Build steps locally; submit one transaction.
No changes unless commit is explicitly True; a designated test RVT is required.
"""
from kalidis_client import Engine
import math

def rectangular_room(level_id, wall_type_id, floor_type_id, x, y, width, length, height, name, include_floor=True):
    vals = [x,y,width,length,height]
    if not all(isinstance(v,(int,float)) and math.isfinite(v) for v in vals):
        raise ValueError("Finite dimensions required")
    if not (100 <= width <= 100000 and 100 <= length <= 100000 and 100 <= height <= 30000):
        raise ValueError("Dimensions out of range")
    pts=[dict(x=x,y=y),dict(x=x+width,y=y),dict(x=x+width,y=y+length),dict(x=x,y=y+length)]
    steps=[dict(action="wall",levelId=level_id,typeId=wall_type_id,heightMm=height,start=pts[i],end=pts[(i+1)%4],alias=f"wall{i+1}") for i in range(4)]
    if include_floor:
        if floor_type_id is None: raise ValueError("floor type required")
        steps.append(dict(action="floor",levelId=level_id,typeId=floor_type_id,points=pts,alias="floor"))
    steps.append(dict(action="room",levelId=level_id,point=dict(x=x+width/2,y=y+length/2),name=name,alias="room"))
    return steps

def hosted_opening(steps, level_id, type_id, wall_alias, x, y, z):
    if not any(s.get("alias")==wall_alias and s.get("action")=="wall" for s in steps):
        raise ValueError("Unknown wall alias")
    return [*steps,dict(action="hosted_component",levelId=level_id,typeId=type_id,hostId="$"+wall_alias,point=dict(x=x,y=y,z=z))]

def execute(expected_path, steps, commit=False):
    return Engine(read_only=False).batch(expected_path,steps,commit=commit)

if __name__=="__main__":
    # Offline tests only: never call a live model from this entry point.
    s=rectangular_room(1,2,3,0,0,4000,5000,3000,"Sala")
    assert len(s)==6 and s[-1]["point"]=={"x":2000,"y":2500}
    assert s[3]["end"]==s[0]["start"]
    t=hosted_opening(s,1,4,"wall1",1000,0,0)
    assert len(t)==7 and t[-1]["hostId"]=="$wall1" and len(s)==6
    try: rectangular_room(1,2,3,0,0,-1,5000,3000,"Invalid")
    except ValueError: pass
    else: raise AssertionError("Invalid size accepted")
    print("RECIPE_TESTS=PASS")
