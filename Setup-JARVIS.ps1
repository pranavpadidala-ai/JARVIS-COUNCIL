[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$solution = Join-Path $root "JARVIS.Council.sln"
$appProject = Join-Path $root "src\Council.Desktop\Council.Desktop.csproj"
$logPath = Join-Path $root "jarvis-setup.log"

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Refresh-Path {
    $machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = "$machinePath;$userPath"
}

function Ensure-DotNet8 {
    $sdks = @()
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        $sdks = & dotnet --list-sdks 2>$null
    }
    if ($sdks -match '^8\.') {
        Write-Host ".NET 8 SDK is already installed."
        return
    }

    Write-Step "Installing the .NET 8 SDK"
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($winget) {
        & $winget.Source install --id Microsoft.DotNet.SDK.8 --exact --accept-package-agreements --accept-source-agreements
        if ($LASTEXITCODE -eq 0) {
            Refresh-Path
            if (Get-Command dotnet -ErrorAction SilentlyContinue) {
                $sdks = & dotnet --list-sdks 2>$null
                if ($sdks -match '^8\.') { return }
            }
        }
        Write-Warning "winget did not make the .NET 8 SDK available; trying Microsoft's official installer."
    }

    $installDir = Join-Path $env:LOCALAPPDATA "Programs\dotnet"
    New-Item -ItemType Directory -Force -Path $installDir | Out-Null
    $installer = Join-Path $env:TEMP "dotnet-install-jarvis.ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installer
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer -Channel 8.0 -InstallDir $installDir
    if ($LASTEXITCODE -ne 0) { throw "The official .NET 8 SDK installer failed." }
    [Environment]::SetEnvironmentVariable("Path", "$installDir;$([Environment]::GetEnvironmentVariable('Path','User'))", "User")
    $env:Path = "$installDir;$env:Path"
    if (-not (Test-Path (Join-Path $installDir "dotnet.exe"))) {
        throw ".NET installation completed but dotnet.exe was not found in $installDir."
    }
}

try {
    Start-Transcript -Path $logPath -Append | Out-Null
    Write-Host "JARVIS folder: $root"
    if (-not (Test-Path $solution)) { throw "JARVIS.Council.sln was not found. Extract the complete GitHub ZIP before running setup." }
    if (-not (Test-Path $appProject)) { throw "The JARVIS desktop application project was not found." }

    Ensure-DotNet8
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw ".NET 8 SDK is not available. Restart Windows and run Setup-JARVIS.bat again."
    }

    Write-Step "Restoring JARVIS dependencies"
    & dotnet restore $solution
    if ($LASTEXITCODE -ne 0) { throw "Dependency restore failed. Check jarvis-setup.log and your internet connection." }

    Write-Step "Building JARVIS"
    & dotnet build $solution --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Build failed. Check jarvis-setup.log for compiler errors." }

    if (-not $SkipTests) {
        Write-Step "Running automated tests"
        & dotnet test $solution --configuration Release --no-build
        if ($LASTEXITCODE -ne 0) { throw "One or more tests failed. JARVIS was built, but setup is not marked successful." }
    }

    Write-Step "Creating your desktop shortcut"
    $desktop = [Environment]::GetFolderPath("Desktop")
    if ([string]::IsNullOrWhiteSpace($desktop)) { throw "Could not locate the Windows Desktop folder." }
    $shortcutPath = Join-Path $desktop "JARVIS.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = Join-Path $env:SystemRoot "System32\cmd.exe"
    $shortcut.Arguments = '/c ""' + (Join-Path $root "Start-JARVIS.bat") + '""'
    $shortcut.WorkingDirectory = $root
    $shortcut.Description = "Start JARVIS personal AI assistant"
    $shortcut.IconLocation = "$env:SystemRoot\System32\shell32.dll,44"
    $shortcut.Save()
    Write-Host "Desktop shortcut created: $shortcutPath" -ForegroundColor Green

    Write-Host ""
    Write-Host "JARVIS setup completed successfully." -ForegroundColor Green
    Write-Host "You can start JARVIS any time by double-clicking the JARVIS desktop icon."
    Write-Host "No local AI models or paid API subscriptions were installed by this setup."
    if (-not $NoLaunch) {
        Write-Step "Starting JARVIS"
        & dotnet run --project $appProject --configuration Release --no-build
        if ($LASTEXITCODE -ne 0) { throw "JARVIS exited with an error. Check the message above." }
    }
}
catch {
    Write-Host ""
    Write-Host "SETUP ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Diagnostic log: $logPath"
    exit 1
}
finally {
    try { Stop-Transcript | Out-Null } catch {}
}
