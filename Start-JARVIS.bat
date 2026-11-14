@echo off
setlocal
cd /d "%~dp0"
set "JARVIS_EXE=%~dp0src\Council.Desktop\bin\Release\net8.0-windows\Council.Desktop.exe"
if not exist "%JARVIS_EXE%" (
  echo JARVIS desktop application was not built. Run Setup-JARVIS.ps1 first.
  pause
  exit /b 1
)
start "" "%JARVIS_EXE%"
exit /b 0
