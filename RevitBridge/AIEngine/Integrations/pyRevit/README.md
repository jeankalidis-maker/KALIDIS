# Kalidis pyRevit bridge prototype
Separate original read-only ribbon extension. No upstream source copied. Uses public pyRevit bundle conventions: https://docs.pyrevitlabs.io/extensions/ .
Requires a separately installed compatible pyRevit host. Add the parent directory to Custom Extension Directories, reload pyRevit, and click Kalidis > Auditoria > Modelo. No automatic installation, HTTP listener, arbitrary execution, or project changes.
This is a source-only integration candidate; no pyRevit host was found in the inspected installation locations on the test workstation. Actual host validation pending.

Milestone 141: pyRevit 7.1 installed, loader signature valid, local extension directory registered through the supported CLI. Agent host enabled, policy ask, reckless and user skills disabled. Reload pyRevit or restart Revit to activate; actual ribbon and agent execution tests pending. No remote agent connector registered.
