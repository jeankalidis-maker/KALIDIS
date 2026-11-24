"""Candidate screening from MEP topology; no automatic connection.
Only coincident open ends with same domain/profile are returned.
Axis orientation, dimensions and routing still require live checks.
"""
from itertools import combinations
from math import dist

def candidates(topology, tolerance_mm=0.1):
    if not 0 <= tolerance_mm <= 10:
        raise ValueError("Invalid tolerance")
    ends=[x for x in topology["connectors"] if not x["isConnected"]]
    rows=[]
    for a,b in combinations(ends,2):
        if a["ownerId"]==b["ownerId"] or a["domain"]!=b["domain"] or a["shape"]!=b["shape"]:
            continue
        gap=dist(a["originMm"],b["originMm"])
        if gap<=tolerance_mm:
            rows.append(dict(firstId=a["ownerId"],firstConnector=a["connectorId"],secondId=b["ownerId"],secondConnector=b["connectorId"],gapMm=gap,needsAxisAndSizeCheck=True))
    return sorted(rows,key=lambda x:x["gapMm"])

if __name__=="__main__":
    a=dict(ownerId=1,connectorId=0,domain="Piping",shape="Round",isConnected=False,originMm=[0,0,0])
    b=dict(a,ownerId=2,originMm=[0,0,0.05])
    c=dict(a,ownerId=3,domain="Hvac")
    assert len(candidates({"connectors":[a,b,c]}))==1
    assert not candidates({"connectors":[a,dict(b,isConnected=True)]})
    print("MEP_CANDIDATE_TESTS=PASS")
