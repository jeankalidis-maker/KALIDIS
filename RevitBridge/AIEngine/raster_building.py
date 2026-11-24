"""Turn a detected rectangular raster with one vertical partition gap into a native building.
Explicitly confirmed door candidate and compatible native types required.
"""
from image_to_walls import analyze
from reviewed_client import execute
from pathlib import Path
from math import isclose

def arguments(report,level,wall_type,floor_type,door_type,height,offset,confirm_door=False):
    if confirm_door is not True:raise ValueError("Door gap interpretation must be explicitly confirmed")
    lines=report["lines"];hs=[a for a in lines if a["axis"]=="H"];vs=[a for a in lines if a["axis"]=="V"]
    if len(hs)!=2 or len(vs)!=4 or len(report["gapCandidates"])!=1:raise ValueError("Expected rectangular plan with one split divider")
    x0,x1=hs[0]["lo"],hs[0]["hi"];y0,y1=hs[0]["center"],hs[1]["center"];gap=report["gapCandidates"][0]
    if hs[1]["lo"]!=x0 or hs[1]["hi"]!=x1 or gap["axis"]!="V":raise ValueError("Nonrectangular plan")
    outer=[v for v in vs if v["center"] in (x0,x1)]
    inner=[v for v in vs if v["center"]==gap["center"]]
    if len(outer)!=2 or {v["center"] for v in outer}!={x0,x1} or any(v["lo"]!=y0 or v["hi"]!=y1 for v in outer):raise ValueError("Invalid exterior")
    if len(inner)!=2 or inner[0]["lo"]!=y0 or inner[1]["hi"]!=y1 or inner[0]["hi"]!=gap["fromPx"] or inner[1]["lo"]!=gap["toPx"]:raise ValueError("Invalid divider")
    scale=report["mmPerPixel"];h=report["sizePx"][1]
    X=lambda x:offset[0]+x*scale
    Y=lambda y:offset[1]+(h-1-y)*scale
    return dict(levelId=level,wallTypeId=wall_type,floorTypeId=floor_type,doorTypeId=door_type,heightMm=height,minXmm=X(x0),maxXmm=X(x1),minYmm=Y(y1),maxYmm=Y(y0),dividerXmm=X(gap["center"]),doorYmm=Y((gap["fromPx"]+gap["toPx"])/2),doorWidthMm=gap["gapMm"])
