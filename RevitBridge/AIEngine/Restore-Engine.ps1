param([Parameter(Mandatory=$true)][string]$BackupName,[switch]$Restore)
$ErrorActionPreference='Stop'
$homeDir=Join-Path $env:LOCALAPPDATA 'KalidisAIEngine'
$manifest=Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2025\Kalidis.AIEngine.addin'
if($BackupName -notmatch '^manifest-backup-[a-fA-F0-9]{32}\.xml$'){throw 'Expected exact manifest backup filename'}
$backup=Join-Path $homeDir $BackupName
function Check-Manifest([string]$file){
 if(!(Test-Path -LiteralPath $file -PathType Leaf)){throw 'Manifest missing'}
 if((Get-Item -LiteralPath $file).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse point refused'}
 [xml]$xml=Get-Content -LiteralPath $file -Raw
 $nodes=@($xml.RevitAddIns.AddIn)
 if($nodes.Count -ne 1 -or $nodes[0].AddInId -ne 'c579f6e5-49f4-4a65-a576-ea99bca89f3d' -or $nodes[0].FullClassName -ne 'Kalidis.AIEngine.Application'){throw 'Manifest identity mismatch'}
 $dll=[string]$nodes[0].Assembly
 $versionRoot=Join-Path $homeDir 'versions'
 if(!$dll.StartsWith($versionRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Only versioned assemblies supported'}
 $hash=Split-Path (Split-Path $dll -Parent) -Leaf
 if($hash -notmatch '^[a-fA-F0-9]{64}$' -or $dll -ne (Join-Path (Join-Path $versionRoot $hash) 'Kalidis.AIEngine.dll')){throw 'Version path invalid'}
 foreach($p in @($homeDir,$versionRoot,(Split-Path $dll -Parent),$dll)){
  if((Get-Item -LiteralPath $p).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse point refused'}
 }
 if((Get-FileHash -LiteralPath $dll).Hash -ne $hash){throw 'DLL hash mismatch'}
 return [pscustomobject]@{assembly=$dll;sha256=$hash}
}
$old=Check-Manifest $manifest
$target=Check-Manifest $backup
if(!$Restore){[pscustomobject]@{ready=$true;current=$old;target=$target;revitRunning=[bool](Get-Process Revit -ErrorAction SilentlyContinue)}|ConvertTo-Json -Depth 4;return}
if(Get-Process Revit -ErrorAction SilentlyContinue){throw 'Close Revit normally before restoring'}
if($old.sha256 -eq $target.sha256){throw 'Requested version is already registered'}
$temporary=$manifest+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
$undo=Join-Path $homeDir ('manifest-backup-'+[guid]::NewGuid().ToString('N')+'.xml')
try{
 Copy-Item -LiteralPath $backup -Destination $temporary
 $check=Check-Manifest $temporary
 if($check.sha256 -ne $target.sha256){throw 'Backup copy changed'}
 if(Get-Process Revit -ErrorAction SilentlyContinue){throw 'Revit started during restore'}
 [IO.File]::Replace($temporary,$manifest,$undo)
 $final=Check-Manifest $manifest
 if($final.sha256 -ne $target.sha256){throw 'Restored manifest mismatch'}
 [pscustomobject]@{restored=$true;activeAfterRestart=$true;assembly=$final.assembly;sha256=$final.sha256;backup=$undo}|ConvertTo-Json
}catch{
 if(Test-Path -LiteralPath $undo){Copy-Item -LiteralPath $undo -Destination $manifest -Force}
 throw
}finally{if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}}
