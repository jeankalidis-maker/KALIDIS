param([switch]$Stage)
$ErrorActionPreference='Stop'
$homeDir=Join-Path $env:LOCALAPPDATA 'KalidisAIEngine'
$manifest=Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2025\Kalidis.AIEngine.addin'
$source=Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\Kalidis.AIEngine.dll'
if(!(Test-Path -LiteralPath $source)){throw 'Build release first'}
[xml]$xml=Get-Content -LiteralPath $manifest -Raw
$nodes=@($xml.RevitAddIns.AddIn)
if($nodes.Count -ne 1 -or $nodes[0].AddInId -ne 'c579f6e5-49f4-4a65-a576-ea99bca89f3d' -or $nodes[0].FullClassName -ne 'Kalidis.AIEngine.Application'){throw 'Manifest identity mismatch'}
$old=[string]$nodes[0].Assembly
if(!$old.StartsWith($homeDir+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Existing assembly outside engine directory'}
$hash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$versionDir=Join-Path (Join-Path $homeDir 'versions') $hash
$target=Join-Path $versionDir 'Kalidis.AIEngine.dll'
if(!$Stage){[pscustomobject]@{ready=$true;current=$old;target=$target;sha256=$hash;revitRunning=[bool](Get-Process Revit -ErrorAction SilentlyContinue)}|ConvertTo-Json;return}
New-Item -ItemType Directory -Path $versionDir -Force|Out-Null
if(!(Test-Path -LiteralPath $target)){Copy-Item -LiteralPath $source -Destination $target}
if((Get-FileHash -LiteralPath $target).Hash -ne $hash){throw 'Staged assembly hash mismatch'}
$backup=Join-Path $homeDir ('manifest-backup-'+[Guid]::NewGuid().ToString('N')+'.xml')
$temporary=$manifest+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
$nodes[0].SelectSingleNode('Assembly').InnerText=[string]$target
$xml.Save($temporary)
try{
 [System.IO.File]::Replace($temporary,$manifest,$backup)
 [xml]$verified=Get-Content -LiteralPath $manifest -Raw
 if([string]$verified.RevitAddIns.AddIn.Assembly -ne $target){throw 'Manifest verification failed'}
}catch{
 if(Test-Path -LiteralPath $backup){Copy-Item -LiteralPath $backup -Destination $manifest -Force}
 throw
}finally{
 if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}
}
[pscustomobject]@{staged=$true;activeAfterRestart=$true;previousAssembly=$old;stagedAssembly=$target;backup=$backup;sha256=$hash;revitRunning=[bool](Get-Process Revit -ErrorAction SilentlyContinue)}|ConvertTo-Json
