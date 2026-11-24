# Read-only UI context for the current Revit session; never clicks or sends keys.
param([int]$ExpectedProcessId=0)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$procs=@(Get-Process Revit -ErrorAction SilentlyContinue | Where-Object {$_.MainWindowHandle -ne 0})
if($ExpectedProcessId -ne 0){$procs=@($procs | Where-Object {$_.Id -eq $ExpectedProcessId})}
if($procs.Count -ne 1){throw 'Expected exactly one Revit main window; specify -ExpectedProcessId'}
$p=$procs[0]
$root=[System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
if($root.Current.ProcessId -ne $p.Id){throw 'UI process mismatch'}
$pattern=$null
$hasWindow=$root.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern,[ref]$pattern)
$modal=$null
if($hasWindow){$modal=$pattern.Current.IsModal}
[pscustomobject]@{ok=$true;pid=$p.Id;windowName=$root.Current.Name;automationId=$root.Current.AutomationId;className=$root.Current.ClassName;enabled=$root.Current.IsEnabled;offscreen=$root.Current.IsOffscreen;responding=$p.Responding;mainWindowModal=$modal;readOnly=$true}|ConvertTo-Json -Compress
