@echo off
setlocal
cd /d "%~dp0"
if not exist "src\Council.App\Council.App.csproj" (
  echo JARVIS application files were not found. Run Setup-JARVIS.bat from the extracted repository folder.
  pause
  exit /b 1
)
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET SDK was not found. Run Setup-JARVIS.bat first.
  pause
  exit /b 1
)
dotnet run --project "src\Council.App\Council.App.csproj" --configuration Release --no-build
if errorlevel 1 (
  echo.
  echo JARVIS stopped with an error. Run Setup-JARVIS.bat again or inspect jarvis-setup.log.
  pause
)
