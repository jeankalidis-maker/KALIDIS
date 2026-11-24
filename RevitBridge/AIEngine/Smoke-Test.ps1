param([Parameter(Mandatory=$true)][string]$Template)
$ErrorActionPreference='Stop'
$homeDir=Join-Path $env:LOCALAPPDATA 'KalidisAIEngine'
$headers=@{Authorization=('Bearer '+(Get-Content (Join-Path $homeDir 'token.txt') -Raw).Trim())}
function Invoke-Engine([hashtable]$Body){
 $r=Invoke-RestMethod 'http://127.0.0.1:7895/execute' -Method Post -Headers $headers -ContentType 'application/json' -Body ($Body|ConvertTo-Json -Depth 30 -Compress) -TimeoutSec 35
 if(-not $r.ok){throw ('Engine failed: '+($r|ConvertTo-Json -Depth 10 -Compress))}
 return $r
}
$catalog=Invoke-Engine @{action='catalog'}
foreach($name in @('new_test_project','open_test_project','edit_elements','extensions','element_info','views')){
 if($catalog.implemented -notcontains $name){throw "Installed DLL outdated: missing $name"}
}
$file='Smoke-'+[Guid]::NewGuid().ToString('N')+'.rvt'
$new=Invoke-Engine @{action='new_test_project';template=$Template;fileName=$file}
$opened=Invoke-Engine @{action='open_test_project';fileName=$file}
$snap=Invoke-Engine @{action='snapshot'}
if($snap.path -ne $new.path -or $opened.path -ne $new.path){throw 'Test document mismatch'}
$level=$snap.levels|Select-Object -First 1
$wall=$snap.wallTypes|Where-Object {$_.kind -eq 'Basic'}|Select-Object -First 1
if(!$level -or !$wall){throw 'Template missing level or basic wall'}
$steps=@(@{action='level';name='Kalidis smoke level';elevationMm=7000;alias='level'},@{action='wall';levelId='$level';typeId=$wall.id;heightMm=3000;start=@{x=0;y=0};end=@{x=4000;y=0};alias='wall'})
$dry=Invoke-Engine @{action='batch';expectedPath=$new.path;steps=$steps;commit=$false}
if(!$dry.verified -or $dry.committed){throw 'Rollback verification failed'}
$write=Invoke-Engine @{action='batch';expectedPath=$new.path;steps=$steps;commit=$true}
if(!$write.verified -or !$write.committed){throw 'Commit verification failed'}
$info=Invoke-Engine @{action='element_info';ids=@($write.aliases.wall)}
$edit=Invoke-Engine @{action='edit_elements';expectedPath=$new.path;steps=@(@{action='move';elementId=$write.aliases.wall;offset=@{x=0;y=1000;z=0}});commit=$false}
$views=Invoke-Engine @{action='views'}
$extension=Invoke-Engine @{action='extensions'}
$categories=Invoke-Engine @{action='list_categories'}
$phases=Invoke-Engine @{action='list_phases'}
$warnings=Invoke-Engine @{action='audit_warnings';limit=10}
$span=Invoke-Engine @{action='audit_level_span';limit=10}
$doors=Invoke-Engine @{action='get_doors';limit=10}
$native=Invoke-Engine @{action='native_status';query='Dynamo'}
if($native.matched -lt 1){throw 'Native discovery failed'}
$printable=$views.views|Where-Object {$_.kind -eq 'FloorPlan'}|Select-Object -First 1
if(!$printable){throw 'No floor plan for preview'}
$preview=Invoke-Engine @{action='preview_view';expectedPath=$new.path;viewId=$printable.id}
if(!$preview.files -or !(Test-Path -LiteralPath $preview.files[0])){throw 'Missing PNG output'}
$save=Invoke-Engine @{action='save_test';expectedPath=$new.path}
[pscustomobject]@{ok=$true;testPath=$new.path;rollback=$dry.verified;commit=$write.verified;wallId=$write.aliases.wall;parameters=$info.elements[0].parameters.Count;edit=$edit.status;views=$views.views.Count;extensionRegistry=$extension.ok;categories=$categories.count;phases=$phases.count;warnings=$warnings.total;levelSpan=$span.total;doors=$doors.count;preview=$preview.files[0];save=$save.ok}|ConvertTo-Json -Compress
