"""Reconstruct reviewed orthogonal raster geometry; caller confirms perimeter, room seeds and door gaps."""
from image_to_walls import analyze
from math import isclose
def arguments(report,perimeter,partitions,rooms,level,wall_type,floor_type,door_type,height,offset,confirmed_gaps):
    def key(a,b):
        if a[0]==b[0]:return ('V',a[0],min(a[1],b[1]),max(a[1],b[1]))
        if a[1]==b[1]:return ('H',a[1],min(a[0],b[0]),max(a[0],b[0]))
        raise ValueError("Orthogonal only")
    edges=list(zip(perimeter,perimeter[1:]+perimeter[:1]))+[(p[0],p[1]) for p in partitions]
    keys=[key(*e) for e in edges]
    source=[(x['axis'],x['center'],x['lo'],x['hi']) for x in report['lines']]
    gaps=report['gapCandidates']
    if sorted(confirmed_gaps)!=list(range(len(gaps))):raise ValueError("Explicit confirmation of every candidate gap required")
    for g in gaps:source.append((g['axis'],g['center'],g['fromPx'],g['toPx']))
    # Each detected span or confirmed gap belongs to exactly one reviewed centerline.
    assigned=[[] for _ in keys]
    for axis,c,lo,hi in source:
        matches=[i for i,k in enumerate(keys) if k[:2]==(axis,c) and k[2]<=lo and hi<=k[3]]
        if len(matches)!=1:raise ValueError("Ambiguous or unmatched raster span")
        assigned[matches[0]].append((lo,hi))
    for k,spans in zip(keys,assigned):
        at=k[2]
        for lo,hi in sorted(spans):
            if lo>at or hi<at:raise ValueError("Missing raster coverage")
            at=max(at,hi)
        if at!=k[3]:raise ValueError("Incomplete reviewed edge")
    scale=report['mmPerPixel'];h=report['sizePx'][1]
    def p(q):return [offset[0]+scale*q[0],offset[1]+scale*(h-1-q[1])]
    doors=[]
    for g in gaps:
        i=next(i for i,k in enumerate(keys) if k[:2]==(g['axis'],g['center']) and k[2]<=g['fromPx'] and g['toPx']<=k[3])
        mid=(g['fromPx']+g['toPx'])/2
        doors.append(dict(wallIndex=i,point=p([g['center'],mid] if g['axis']=='V' else [mid,g['center']]),widthMm=g['gapMm']))
    return dict(levelId=level,wallTypeId=wall_type,floorTypeId=floor_type,doorTypeId=door_type,heightMm=height,perimeter=[p(q) for q in perimeter],partitions=[dict(start=p(x),end=p(y)) for x,y in partitions],rooms=[p(q) for q in rooms],doors=doors)
