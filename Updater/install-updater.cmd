@echo off
setlocal

set "REPO=C:\Users\naose\KALIDIS"
set "PROJECT=%REPO%\Updater\Kalidis.Updater\Kalidis.Updater.csproj"
set "INSTALL=C:\KALIDIS\Updater\App"
set "RUNNER=C:\KALIDIS\Updater\run-updater.cmd"
set "TASK=KALIDIS Updater"

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

schtasks /Delete /TN "%TASK%" /F >nul 2>&1
schtasks /Create /SC ONLOGON /TN "%TASK%" /TR "cmd.exe /c \"%RUNNER%\"" /F
if errorlevel 1 (
  echo [AVISO] Nao foi possivel criar a tarefa de inicializacao automatica.
  echo Execute manualmente: "%RUNNER%"
) else (
  echo [KALIDIS] Inicializacao automatica configurada.
)

call "%RUNNER%"

echo.
echo [KALIDIS] Updater instalado e iniciado.
echo Estado: C:\KALIDIS\Updater\state.json
echo Log:    C:\KALIDIS\Updater\updater.log
exit /b 0
