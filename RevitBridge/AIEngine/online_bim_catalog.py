"""Pinned public BIM sample catalogue. Download to quarantine only; never auto-load.
The upstream MIT license must accompany redistributed files.
"""
import urllib.request, urllib.parse, hashlib, json, pathlib, uuid, argparse
REPO="magnetar-io/revit-test-datasets"
REV="fd755e18cec40ed016d06b082f420d796571fef1"
ITEMS=[dict(name="Column Square",path="Revit/Families/Columns/Column_Sqaure.rfa",sha256="0660610efb4b6c8dd8717be4c4c1b6f74f044643c7d427590e6c122a02720bf9",bytes=360448,license="MIT",source=REPO,revision=REV)]
def search(query):
    return [r for r in ITEMS if all(w.casefold() in (r["name"]+" "+r["path"]).casefold() for w in query.split())]
def download(item,root):
    if item not in ITEMS:raise ValueError("Only reviewed pinned catalogue entries")
    root=pathlib.Path(root)
    if not root.is_dir() or root.is_symlink() or root.name!="Sandbox":raise ValueError("Sandbox directory required")
    dest=root/("OnlineQuarantine-"+uuid.uuid4().hex)
    dest.mkdir()
    url="https://media.githubusercontent.com/media/"+REPO+"/"+REV+"/"+urllib.parse.quote(item["path"])
    req=urllib.request.Request(url,headers={"User-Agent":"KalidisBIM-Research/1"})
    with urllib.request.urlopen(req,timeout=25) as response:
        if urllib.parse.urlparse(response.geturl()).hostname!="media.githubusercontent.com":raise ValueError("Unexpected download destination")
        data=response.read(item["bytes"]+1)
    if len(data)!=item["bytes"] or hashlib.sha256(data).hexdigest()!=item["sha256"] or data[:8]!=bytes.fromhex("d0cf11e0a1b11ae1"):
        raise ValueError("Size, hash or compound-file header mismatch")
    file=dest/pathlib.Path(item["path"]).name
    with file.open("xb") as f:f.write(data)
    license_url="https://raw.githubusercontent.com/"+REPO+"/"+REV+"/LICENSE"
    with urllib.request.urlopen(license_url,timeout=15) as r:
        license_text=r.read(8192)
    if b"MIT License" not in license_text or b"magnetar-io" not in license_text:raise ValueError("License mismatch")
    (dest/"LICENSE.txt").write_bytes(license_text)
    result=dict(item,path=str(file),status="quarantined",geometryVerified=False,loaded=False)
    (dest/"manifest.json").write_text(json.dumps(result,indent=2),encoding="utf-8")
    return result
if __name__=="__main__":
    p=argparse.ArgumentParser();p.add_argument("--query",required=True);p.add_argument("--download-to")
    a=p.parse_args();items=search(a.query);print(json.dumps(items),flush=True)
    if a.download_to:
        if len(items)!=1:raise ValueError("Select exactly one entry")
        print(json.dumps(download(items[0],a.download_to)),flush=True)
