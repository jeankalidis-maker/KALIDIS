# -*- coding: utf-8 -*-
"""Read-only inspection of the registered Kalidis ribbon; no clicks."""
import os
import clr
clr.AddReference("AdWindows")
from Autodesk.Windows import ComponentManager
from pyrevit.loader import sessionmgr
extension = os.path.normcase(os.path.normpath(r"C:\KALIDIS\AIEngineDevelopment\Source\RevitBridge\AIEngine\Integrations\pyRevit\Kalidis.extension")) + os.sep
tabs = [t for t in ComponentManager.Ribbon.Tabs if t.Title == "Kalidis"]
result = {
    "tab_visible": bool(tabs) and bool(tabs[0].IsVisible),
    "panels": [p.Source.Title for p in tabs[0].Panels] if tabs else [],
    "commands": [c.typename for c in sessionmgr.find_all_commands(cache=False) if os.path.normcase(c.script or "").startswith(extension)]
}
