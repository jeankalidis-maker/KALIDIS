param([ValidateSet('health','catalog','snapshot','test_wall')][string]$Action='snapshot',[string]$ExpectedPath,[long]$LevelId,[long]$WallTypeId,[switch]$Commit)
$ErrorActionPreference='Stop'
$homeDir=Join-Path $env:LOCALAPPDATA 'KalidisAIEngine'
$headers=@{Authorization=('Bearer '+(Get-Content (Join-Path $homeDir 'token.txt') -Raw).Trim())}
$base='http://127.0.0.1:7895'
if($Action -eq 'health'){Invoke-RestMethod -Uri "$base/health" -Headers $headers | ConvertTo-Json -Depth 20; return}
$body=@{action=$Action}
if($Action -eq 'test_wall'){
 if(-not $ExpectedPath -or $LevelId -le 0 -or $WallTypeId -le 0){throw 'Informe caminho da copia, LevelId e WallTypeId'}
 $body.expectedPath=$ExpectedPath; $body.levelId=$LevelId; $body.wallTypeId=$WallTypeId; $body.commit=[bool]$Commit
}
Invoke-RestMethod -Uri "$base/execute" -Method Post -Headers $headers -ContentType 'application/json' -Body ($body | ConvertTo-Json -Compress) -TimeoutSec 40 | ConvertTo-Json -Depth 20
