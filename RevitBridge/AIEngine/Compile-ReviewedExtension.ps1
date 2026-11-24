param([Parameter(Mandatory=$true)][string]$Source)
$ErrorActionPreference='Stop'
$sourcePath=(Resolve-Path -LiteralPath $Source).Path
if([IO.Path]::GetExtension($sourcePath) -ne '.cs'){throw 'Select reviewed C# source'}
$homeDir=Join-Path $env:LOCALAPPDATA 'KalidisAIEngine'
$sourceHash=(Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
$manifest=Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2025\Kalidis.AIEngine.addin'
[xml]$xml=Get-Content -LiteralPath $manifest -Raw
$engine=[string]$xml.RevitAddIns.AddIn.Assembly
if(!(Test-Path -LiteralPath $engine) -or !$engine.StartsWith($homeDir+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Engine path invalid'}
$engineHash=(Get-FileHash -LiteralPath $engine).Hash
$name='Kalidis.Reviewed.'+$sourceHash.Substring(0,12)+'.'+$engineHash.Substring(0,12)
$work=Join-Path (Join-Path $homeDir 'ExtensionBuilds') ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work|Out-Null
Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $work 'Operation.cs')
if((Get-FileHash (Join-Path $work 'Operation.cs')).Hash -ne $sourceHash){throw 'Source changed'}
$escaped=[Security.SecurityElement]::Escape($engine)
$project=@"
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0-windows</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><PlatformTarget>x64</PlatformTarget><AssemblyName>$name</AssemblyName><NuGetAudit>false</NuGetAudit></PropertyGroup>
<ItemGroup><Reference Include="Kalidis.AIEngine"><HintPath>$escaped</HintPath><Private>false</Private></Reference><Reference Include="RevitAPI"><HintPath>C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll</HintPath><Private>false</Private></Reference><Reference Include="RevitAPIUI"><HintPath>C:\Program Files\Autodesk\Revit 2025\RevitAPIUI.dll</HintPath><Private>false</Private></Reference></ItemGroup>
</Project>
"@
$projectPath=Join-Path $work 'Operation.csproj'
[IO.File]::WriteAllText($projectPath,$project)
[IO.File]::WriteAllText((Join-Path $work 'NuGet.Config'),'<configuration><packageSources><clear /></packageSources></configuration>')
dotnet build $projectPath -c Release --nologo --configfile (Join-Path $work 'NuGet.Config')
if($LASTEXITCODE -ne 0){throw 'Compilation failed'}
$dll=Join-Path $work ('bin\Release\net8.0-windows\'+$name+'.dll')
[pscustomobject]@{compiled=$true;sourceHash=$sourceHash;assembly=$dll;sha256=(Get-FileHash $dll).Hash;approved=$false;note='Review source and approve binary separately. Full trust, not sandboxed.'}|ConvertTo-Json
