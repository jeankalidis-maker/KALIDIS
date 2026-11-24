param([Parameter(Mandatory=$true)][string]$Assembly,[Parameter(Mandatory=$true)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ReviewedSha256)
$ErrorActionPreference='Stop'
$source=(Resolve-Path -LiteralPath $Assembly).Path
$homeDir=Join-Path $env:LOCALAPPDATA 'KalidisAIEngine'
if(!$source.StartsWith((Join-Path $homeDir 'ExtensionBuilds')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Only local compiled extensions'}
$name=[IO.Path]::GetFileName($source)
if($name -notmatch '^Kalidis\.Reviewed\.[A-Fa-f0-9]{12}\.[A-Fa-f0-9]{12}\.dll$'){throw 'Unexpected assembly identity'}
$hash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
if($hash -ne $ReviewedSha256){throw 'Hash differs from reviewed build'}
$folder=Join-Path $homeDir 'ReviewedExtensions'
New-Item -ItemType Directory -Path $folder -Force|Out-Null
if((Get-Item -LiteralPath $folder).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse point refused'}
$target=Join-Path $folder $name
$manifest=Join-Path $folder 'approved.json'
if(Test-Path -LiteralPath $manifest){
 if((Get-Item -LiteralPath $manifest).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Manifest reparse point refused'}
 $data=Get-Content -LiteralPath $manifest -Raw|ConvertFrom-Json
}else{$data=[pscustomobject]@{}}
if($data.PSObject.Properties.Name -contains $name){if($data.$name -ne $hash){throw 'Existing approval differs'}}
else{$data|Add-Member -NotePropertyName $name -NotePropertyValue $hash}
if(Test-Path -LiteralPath $target){
 if((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Target reparse point refused'}
 if((Get-FileHash -LiteralPath $target).Hash -ne $hash){throw 'Existing binary differs'}
}else{[IO.File]::Copy($source,$target,$false)}
if((Get-FileHash -LiteralPath $target).Hash -ne $hash){throw 'Staged hash differs'}
$temp=Join-Path $folder ('approval-'+[Guid]::NewGuid().ToString('N')+'.tmp')
[IO.File]::WriteAllText($temp,($data|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
try{
 if(Test-Path -LiteralPath $manifest){[IO.File]::Replace($temp,$manifest,$null)}else{[IO.File]::Move($temp,$manifest)}
}finally{if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp}}
[pscustomobject]@{approved=$true;assembly=$name;sha256=$hash;trust='Full trust; not a security sandbox'}|ConvertTo-Json
