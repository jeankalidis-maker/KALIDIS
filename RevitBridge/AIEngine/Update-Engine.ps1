param([switch]$Install)
$ErrorActionPreference='Stop'
$homeDir=Join-Path $env:LOCALAPPDATA 'KalidisAIEngine'
$source=Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\Kalidis.AIEngine.dll'
if(!(Test-Path -LiteralPath $source)){throw 'Build release first'}
$target=Join-Path $homeDir 'addin\Kalidis.AIEngine.dll'
if(!$Install){[pscustomobject]@{ready=$true;revitRunning=[bool](Get-Process Revit -ErrorAction SilentlyContinue);source=$source;sha256=(Get-FileHash $source).Hash;target=$target}|ConvertTo-Json;return}
if(Get-Process Revit -ErrorAction SilentlyContinue){throw 'Save and close Revit normally before installing. No process was terminated.'}
if(!(Test-Path -LiteralPath $target)){throw 'Existing installation not found; no changes made'}
$backup=Join-Path $homeDir ('backup-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backup|Out-Null
Copy-Item -LiteralPath $target -Destination $backup
try{
 if(Get-Process Revit -ErrorAction SilentlyContinue){throw 'Revit started during update'}
 Copy-Item -LiteralPath $source -Destination $target -Force
 if((Get-FileHash $source).Hash -ne (Get-FileHash $target).Hash){throw 'Installed binary hash mismatch'}
}catch{Copy-Item -LiteralPath (Join-Path $backup 'Kalidis.AIEngine.dll') -Destination $target -Force;throw}
[pscustomobject]@{installed=$true;backup=$backup;sha256=(Get-FileHash $target).Hash}|ConvertTo-Json
