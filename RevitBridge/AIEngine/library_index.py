"""Search explicit local RFA/RFT libraries without opening or loading any file.
Names and relative paths are indexed, not family parameters or geometry.
"""
from pathlib import Path
import os, re, unicodedata, json, argparse
from datetime import datetime, timezone

def normalized(text):
    return "".join(c for c in unicodedata.normalize("NFKD",text.casefold()) if not unicodedata.combining(c))
def is_link(p):
    return p.is_symlink() or (hasattr(p,"is_junction") and p.is_junction())
def scan(roots, limit=20000):
    rows=[];errors=[]
    def failed(error):errors.append(str(error))
    for value in roots:
        root=Path(value)
        if not root.is_dir() or is_link(root):raise ValueError(f"Invalid root {root}")
        for folder,dirs,files in os.walk(root,followlinks=False,onerror=failed):
            dirs[:]=[d for d in dirs if not is_link(Path(folder)/d)]
            for name in files:
                p=Path(folder)/name
                if p.suffix.lower() not in (".rfa",".rft") or is_link(p):continue
                try:
                    st=p.stat()
                    rows.append(dict(path=str(p),relative=str(p.relative_to(root)),root=str(root),kind=p.suffix.lower()[1:],bytes=st.st_size))
                except OSError as e:errors.append(str(e))
                if len(rows)>=limit:return dict(rows=rows,truncated=True,errors=errors)
    return dict(rows=rows,truncated=False,errors=errors)
def search(index,query,kind="rfa",limit=30):
    words=re.findall(r"\w+",normalized(query))
    if not words or not 1<=limit<=200:raise ValueError("Search terms and limit required")
    scored=[]
    for row in index["rows"]:
        if row["kind"]!=kind:continue
        name=normalized(Path(row["path"]).stem);full=normalized(row["relative"])
        if all(w in full for w in words):
            scored.append((sum(3 if w in name else 1 for w in words),row))
    return [r for _,r in sorted(scored,key=lambda x:(-x[0],x[1]["relative"]))[:limit]]
if __name__=="__main__":
    p=argparse.ArgumentParser()
    p.add_argument("--root",action="append",required=True)
    p.add_argument("--query",required=True)
    p.add_argument("--kind",choices=["rfa","rft"],default="rfa")
    args=p.parse_args()
    idx=scan(args.root)
    print(json.dumps(dict(count=len(idx["rows"]),truncated=idx["truncated"],errors=idx["errors"],matches=search(idx,args.query,args.kind)),ensure_ascii=True))
