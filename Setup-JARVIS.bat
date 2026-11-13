@echo off
setlocal
cd /d "%~dp0"
echo ==========================================
echo          JARVIS - First-time setup
echo ==========================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-JARVIS.ps1"
if errorlevel 1 (
  echo.
  echo Setup did not complete. Read the error above.
  pause
  exit /b 1
)
echo.
echo Setup finished. You can now use Start-JARVIS.bat.
pause
