"""Stage explicitly selected local RFA files without overwriting originals.
No remote download or automatic loading; review the selected file first.
"""
from pathlib import Path
from hashlib import sha256
from tempfile import TemporaryDirectory
import os, shutil

def stage_family(source, sandbox, expected_sha256):
    source=Path(source)
    if source.suffix.lower()!=".rfa" or not source.is_file() or source.is_symlink():
        raise ValueError("Select a local RFA file")
    sandbox=Path(sandbox)
    if not sandbox.is_dir() or sandbox.is_symlink() or (hasattr(sandbox,"is_junction") and sandbox.is_junction()):
        raise ValueError("Sandbox must be a real directory")
    digest=expected_sha256.lower()
    if len(digest)!=64 or any(c not in "0123456789abcdef" for c in digest):
        raise ValueError("Explicit reviewed SHA-256 required")
    dest=sandbox/(source.stem+"-"+digest[:12]+".rfa")
    created=False
    try:
        h=sha256()
        with source.open("rb") as inp:
            with dest.open("xb") as out:
                created=True
                for block in iter(lambda:inp.read(1024*1024),b""):
                    h.update(block);out.write(block)
        if h.hexdigest()!=digest:raise ValueError("File hash mismatch")
        return dest
    except Exception:
        if created:dest.unlink(missing_ok=True)
        raise

if __name__=="__main__":
    with TemporaryDirectory() as tmp:
        root=Path(tmp);src=root/"sample.rfa";src.write_bytes(b"fixture only")
        box=root/"sandbox";box.mkdir()
        good=sha256(src.read_bytes()).hexdigest()
        staged=stage_family(src,box,good)
        assert staged.read_bytes()==src.read_bytes()
        try:stage_family(src,box,good)
        except FileExistsError:pass
        else:raise AssertionError("Overwrite permitted")
        try:stage_family(src,box,"0"*64)
        except ValueError:pass
        else:raise AssertionError("Wrong hash accepted")
        assert len(list(box.iterdir()))==1
    print("LIBRARY_STAGE_TESTS=PASS (fixture)")
