"""Local Python client for Kalidis Revit engine. Python runs outside Revit.
No arbitrary code executes in Revit; scripts compose the structured API.
Requires Python 3.9+ standard library only.
"""
import json
import os
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.error import HTTPError

class Engine:
    def __init__(self, read_only=True):
        self.read_only = read_only
        self.home = Path(os.environ["LOCALAPPDATA"]) / "KalidisAIEngine"
        manifest = json.loads(Path(__file__).with_name("tools.json").read_text(encoding="utf-8-sig"))
        self.tools = {t["name"]: t for t in manifest}

    def call(self, action, **args):
        tool = self.tools[action]
        if self.read_only and not tool["annotations"]["readOnlyHint"]:
            raise PermissionError("Read-only Python session")
        schema = tool["inputSchema"]
        if set(args) - set(schema["properties"]) or set(schema.get("required", [])) - set(args):
            raise ValueError("Invalid or missing arguments")
        token = (self.home / "token.txt").read_text().strip()
        body = json.dumps(dict(args, action=action), allow_nan=False).encode()
        req = Request("http://127.0.0.1:7895/execute", data=body,
                      headers={"Authorization": "Bearer " + token, "Content-Type": "application/json"})
        try:
            with urlopen(req, timeout=35) as response:
                result = json.load(response)
        except HTTPError as exc:
            result = json.load(exc)
        if not result.get("ok"):
            raise RuntimeError(result.get("error", str(result)))
        return result

    def batch(self, expected_path, steps, commit=False):
        return self.call("batch", expectedPath=str(expected_path), steps=steps, commit=commit)

    def snapshot(self):
        return self.call("snapshot")

if __name__ == "__main__":
    s = Engine().snapshot()
    print(json.dumps({"ok": s["ok"], "title": s["title"],
                      "levels": len(s["levels"]), "families": len(s["families"])}, ensure_ascii=True))
