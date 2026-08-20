@echo off
setlocal

set "REPO=C:\Users\naose\KALIDIS"
set "PROJECT=%REPO%\Updater\Kalidis.Updater\Kalidis.Updater.csproj"
set "INSTALL=C:\KALIDIS\Updater\App"
set "RUNNER=C:\KALIDIS\Updater\run-updater.cmd"
set "STARTUP=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"
set "STARTUP_RUNNER=%STARTUP%\KALIDIS-Updater.cmd"

echo [KALIDIS] Preparando updater...
if not exist "C:\KALIDIS\Updater" mkdir "C:\KALIDIS\Updater"

if not exist "%PROJECT%" (
  echo [ERRO] Projeto do updater nao encontrado: %PROJECT%
  exit /b 1
)

echo [KALIDIS] Compilando updater...
dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained false -o "%INSTALL%"
if errorlevel 1 (
  echo [ERRO] Falha ao compilar o updater.
  exit /b 1
)

>"%RUNNER%" echo @echo off
>>"%RUNNER%" echo start "" /min "%INSTALL%\Kalidis.Updater.exe"

if not exist "%STARTUP%" mkdir "%STARTUP%"
>"%STARTUP_RUNNER%" echo @echo off
>>"%STARTUP_RUNNER%" echo call "%RUNNER%"

if exist "%STARTUP_RUNNER%" (
  echo [KALIDIS] Inicializacao automatica configurada para este usuario.
) else (
  echo [ERRO] Nao foi possivel configurar a inicializacao automatica.
  echo Execute manualmente: "%RUNNER%"
  exit /b 1
)

call "%RUNNER%"

echo.
echo [KALIDIS] Updater instalado e iniciado.
echo Inicio automatico: %STARTUP_RUNNER%
echo Estado: C:\KALIDIS\Updater\state.json
echo Log:    C:\KALIDIS\Updater\updater.log
exit /b 0
