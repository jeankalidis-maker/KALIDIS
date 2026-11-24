"""Raster-to-native-wall reconstruction for clean orthogonal black-on-white plans.
Not a general photo, scan, OCR or door/window recognition engine.
Caller supplies calibrated mm/pixel, wall type, level and height. Never commits by default.
"""
from PIL import Image, ImageDraw, ImageChops
from pathlib import Path
from statistics import median
from hashlib import sha256
from math import isclose
import json

def runs(values,min_length):
    start=None
    for i,v in enumerate(values+[False]):
        if v and start is None:start=i
        if not v and start is not None:
            if i-start>=min_length:yield (start,i-1)
            start=None

def detect(im,min_run=40,max_thickness=25):
    w,h=im.size;px=im.load()
    def scan(transpose):
        groups=[];active=[]
        for row in range(w if transpose else h):
            spans=list(runs([px[row,i]==0 if transpose else px[i,row]==0 for i in range(h if transpose else w)],min_run))
            new=[]
            for lo,hi in spans:
                matches=[g for g in active if min(hi,g[-1][2])-max(lo,g[-1][1])+1>=.8*min(hi-lo+1,g[-1][2]-g[-1][1]+1)]
                if len(matches)>1:raise ValueError("Ambiguous raster junction")
                g=matches[0] if matches else []
                if g in new:raise ValueError("Ambiguous branch")
                g.append((row,lo,hi));new.append(g)
            groups.extend(g for g in active if g not in new);active=new
        groups.extend(active)
        result=[]
        for g in groups:
            thickness=len(g)
            if thickness<3:continue
            if thickness>max_thickness:raise ValueError("Stroke too wide; clean centerline plan required")
            axis=(g[0][0]+g[-1][0])/2;lo=median(v[1] for v in g);hi=median(v[2] for v in g)
            result.append(dict(axis="V" if transpose else "H",center=axis,lo=lo,hi=hi,thickness=thickness))
        return result
    lines=scan(False)+scan(True)
    if not 1<=len(lines)<=100:raise ValueError("Expected 1–100 detected walls")
    # Snap ends to nearby perpendicular centerline intersections; preserve open gaps.
    for a in lines:
        for k in ("lo","hi"):
            options=[b["center"] for b in lines if a["axis"]!=b["axis"] and b["lo"]-b["thickness"]<=a["center"]<=b["hi"]+b["thickness"] and abs(a[k]-b["center"])<=max(a["thickness"],b["thickness"])]
            if options:a[k]=min(options,key=lambda v:abs(v-a[k]))
        if a["hi"]<=a["lo"]:raise ValueError("Collapsed line")
    lines.sort(key=lambda a:(a["axis"],a["center"],a["lo"]))
    return lines

def analyze(path,mm_per_pixel):
    path=Path(path)
    if path.suffix.lower()!=".png" or path.stat().st_size>10_000_000:raise ValueError("Bounded PNG only")
    if not 0.1<=mm_per_pixel<=1000:raise ValueError("Explicit scale required")
    with Image.open(path) as src:
        if src.format!="PNG" or max(src.size)>2048 or min(src.size)<80:raise ValueError("Invalid image dimensions")
        im=src.convert("L").point(lambda p:0 if p<128 else 255)
    lines=detect(im)
    predicted=Image.new("L",im.size,255);draw=ImageDraw.Draw(predicted)
    def endpoints(a):
        return [(a["lo"],a["center"]),(a["hi"],a["center"])] if a["axis"]=="H" else [(a["center"],a["lo"]),(a["center"],a["hi"])]
    for a in lines:draw.line(endpoints(a),fill=0,width=a["thickness"])
    diff=ImageChops.difference(im,predicted).histogram()[255]
    ink=im.histogram()[0]
    if not ink or diff/ink>.08:raise ValueError("Raster mismatch exceeds 8%; refuse automatic reconstruction")
    gaps=[]
    for a,b in zip(lines,lines[1:]):
        if a["axis"]==b["axis"] and a["center"]==b["center"] and 500<=(b["lo"]-a["hi"])*mm_per_pixel<=2000:
            gaps.append(dict(axis=a["axis"],center=a["center"],fromPx=a["hi"],toPx=b["lo"],gapMm=(b["lo"]-a["hi"])*mm_per_pixel,note="Candidate gap; door/window type not inferred"))
    return dict(sourceSha256=sha256(path.read_bytes()).hexdigest(),sizePx=list(im.size),mmPerPixel=mm_per_pixel,lines=lines,gapCandidates=gaps,inkMismatchRatio=diff/ink,segmentsPx=[endpoints(a) for a in lines])

def reconstruct(engine,p,report,level,type_id,height_mm,offset_mm,commit=False):
    if Path(p).parent.name!="Sandbox":raise ValueError("Sandbox only")
    if not 500<=height_mm<=10000:raise ValueError("Height")
    scale=report["mmPerPixel"];h=report["sizePx"][1]
    def point(q):return dict(x=offset_mm[0]+scale*q[0],y=offset_mm[1]+scale*(h-1-q[1]),z=0)
    steps=[dict(action="wall",levelId=level,typeId=type_id,heightMm=height_mm,start=point(a),end=point(b)) for a,b in report["segmentsPx"]]
    dry=engine.batch(p,steps,commit=False)
    assert dry["verified"] and not dry["committed"]
    if not commit:return dict(dryRun=True,wallCount=len(steps),plan=steps)
    result=engine.batch(p,steps,commit=True)
    assert result["verified"] and result["committed"]
    from reviewed_client import execute
    g=execute(engine,"geometry_review",dict(ids=result["elementIds"]),p)["result"]["elements"]
    expected=sorted((abs(b[0]-a[0])+abs(b[1]-a[1]))*scale for a,b in report["segmentsPx"])
    actual=sorted(x["curveLengthMm"] for x in g)
    if len(expected)!=len(actual) or any(not isclose(a,b,abs_tol=.1) for a,b in zip(actual,expected)):raise RuntimeError("Committed geometry differs; inspect experimental copy before further changes")
    return dict(ids=result["elementIds"],lengthsMm=actual,wallCount=len(actual),sourceSha256=report["sourceSha256"],gapCandidates=report["gapCandidates"])
