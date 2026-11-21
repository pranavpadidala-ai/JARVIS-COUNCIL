
# JARVIS - Complete PC Environment Scanner
# Read-only diagnostic. Does not install or modify anything.

$ErrorActionPreference = "Continue"

$Root = "E:\JARVIS"
$OutDir = Join-Path $Root "diagnostics"

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$Report = [ordered]@{
    ScanDate = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    Computer = @{}
    Hardware = @{}
    Storage = @()
    DevelopmentTools = @()
    PythonEnvironments = @()
    PythonPackages = @()
    Ollama = @{}
    InstalledApplications = @()
    EngineeringSoftware = @()
    AutomationTools = @()
    EnvironmentChecks = @()
}

function Add-Section {
    param([string]$Name)
    Write-Host "`n========== $Name ==========" -ForegroundColor Cyan
}

function Check-Command {
    param([string]$Name)

    $cmd = Get-Command $Name -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if ($cmd) {
        return @{
            Name = $Name
            Available = $true
            Path = $cmd.Source
            Type = "$($cmd.CommandType)"
        }
    }

    return @{
        Name = $Name
        Available = $false
    }
}

function Run-Command {
    param(
        [string]$Name,
        [string]$Arguments
    )

    $cmd = Get-Command $Name -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if (!$cmd) { return "NOT INSTALLED" }

    try {
        $result = & $Name $Arguments.Split(" ") 2>&1 |
            Select-Object -First 30
        return ($result -join "`n")
    }
    catch {
        return "ERROR: $($_.Exception.Message)"
    }
}

# 1. Computer information
Add-Section "COMPUTER"

$OS = Get-CimInstance Win32_OperatingSystem
$CPU = Get-CimInstance Win32_Processor
$Computer = Get-CimInstance Win32_ComputerSystem

$Report.Computer = @{
    Name = $Computer.Name
    Manufacturer = $Computer.Manufacturer
    Model = $Computer.Model
    Windows = $OS.Caption
    WindowsVersion = $OS.Version
    Build = $OS.BuildNumber
    Architecture = $OS.OSArchitecture
    CPU = $CPU.Name
    PhysicalCores = $CPU.NumberOfCores
    LogicalProcessors = $CPU.NumberOfLogicalProcessors
    RAM_GB = [math]::Round($Computer.TotalPhysicalMemory / 1GB, 2)
    PowerShell = $PSVersionTable.PSVersion.ToString()
}

$Report.Computer | Format-List

# 2. GPU
Add-Section "GRAPHICS"

$Report.Hardware.GPUs = @(
    Get-CimInstance Win32_VideoController |
    Select-Object Name, DriverVersion,
        AdapterRAM, VideoModeDescription
)

$Report.Hardware.GPUs | Format-Table -AutoSize

# 3. Storage
Add-Section "STORAGE"

$Report.Storage = @(
    Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3" |
    ForEach-Object {
        @{
            Drive = $_.DeviceID
            SizeGB = [math]::Round($_.Size / 1GB, 2)
            FreeGB = [math]::Round($_.FreeSpace / 1GB, 2)
        }
    }
)

$Report.Storage | Format-Table -AutoSize

# 4. Development tools
Add-Section "DEVELOPMENT TOOLS"

$ToolNames = @(
    "python", "python3", "py", "pip", "pip3",
    "dotnet", "csc", "msbuild",
    "node", "npm", "npx",
    "java", "javac",
    "git", "gh",
    "docker", "wsl",
    "ollama",
    "code", "devenv",
    "cmake", "gcc", "g++",
    "clang", "rustc", "cargo",
    "go", "ruby",
    "powershell", "pwsh",
    "winget",
    "autohotkey",
    "adb",
    "uv", "conda", "poetry"
)

foreach ($tool in $ToolNames) {
    $result = Check-Command $tool
    $Report.DevelopmentTools += $result

    if ($result.Available) {
        Write-Host "$tool : $($result.Path)" -ForegroundColor Green
    }
}

# 5. .NET
Add-Section ".NET"

$Report.EnvironmentChecks += @{
    Name = ".NET SDKs"
    Result = (Run-Command "dotnet" "--list-sdks")
}

$Report.EnvironmentChecks += @{
    Name = ".NET Runtimes"
    Result = (Run-Command "dotnet" "--list-runtimes")
}

# 6. Python environments and packages
Add-Section "PYTHON ENVIRONMENTS"

$PythonCommands = @("python", "py", "python3")

foreach ($p in $PythonCommands) {
    if (Get-Command $p -ErrorAction SilentlyContinue) {
        try {
            $version = & $p --version 2>&1
            $exe = & $p -c "import sys; print(sys.executable)" 2>&1

            $Report.PythonEnvironments += @{
                Command = $p
                Version = "$version"
                Executable = "$exe"
            }

            Write-Host "$p : $version : $exe"
        }
        catch {}
    }
}

$PythonProbe = @'
import sys, json, importlib.util, subprocess

modules = [
    "torch", "tensorflow", "transformers", "accelerate",
    "ollama", "llama_cpp", "sentence_transformers",
    "langchain", "langgraph", "llama_index",
    "openai", "google.generativeai",
    "cv2", "PIL", "numpy", "scipy", "pandas",
    "matplotlib", "sklearn", "sympy",
    "playwright", "selenium", "pyautogui",
    "pywinauto", "uiautomation", "keyboard", "mouse",
    "pytesseract", "easyocr", "paddleocr",
    "docx", "openpyxl", "pptx", "pypdf",
    "fitz", "pdfplumber", "reportlab",
    "trimesh", "meshio", "vtk",
    "fastapi", "flask", "streamlit",
    "gradio", "nicegui", "PySide6", "PyQt6",
    "sounddevice", "speech_recognition",
    "whisper", "faster_whisper", "librosa",
    "onnxruntime", "ctransformers",
    "einops", "bitsandbytes", "vllm",
    "mcp", "pydantic", "requests",
    "beautifulsoup4", "bs4", "httpx",
    "networkx", "shapely", "ezdxf",
    "ifcopenshell", "openseespy"
]

result = {
    "Python": sys.version,
    "Executable": sys.executable,
    "Modules": {}
}

for m in modules:
    try:
        result["Modules"][m] = bool(importlib.util.find_spec(m))
    except Exception:
        result["Modules"][m] = False

try:
    packages = subprocess.check_output(
        [sys.executable, "-m", "pip", "list", "--format=json"],
        stderr=subprocess.DEVNULL,
        text=True,
        timeout=90
    )
    result["AllInstalledPackages"] = json.loads(packages)
except Exception as e:
    result["PackageScanError"] = str(e)

print(json.dumps(result))
'@

$PythonExecutables = @()

foreach ($p in $PythonCommands) {
    if (Get-Command $p -ErrorAction SilentlyContinue) {
        try {
            $exe = (& $p -c "import sys; print(sys.executable)" 2>$null |
                Select-Object -First 1)

            if ($exe -and $PythonExecutables -notcontains $exe) {
                $PythonExecutables += $exe
            }
        }
        catch {}
    }
}

foreach ($exe in $PythonExecutables) {
    try {
        $json = & $exe -c $PythonProbe 2>$null
        if ($LASTEXITCODE -eq 0) {
            $data = ($json -join "`n") | ConvertFrom-Json

            $Report.PythonPackages += @{
                Executable = $exe
                Modules = $data.Modules
                AllInstalledPackages = $data.AllInstalledPackages
            }

            Write-Host "`nPython: $exe" -ForegroundColor Green

            $data.Modules.PSObject.Properties |
                Where-Object { $_.Value -eq $true } |
                ForEach-Object {
                    Write-Host "  AVAILABLE: $($_.Name)" -ForegroundColor Green
                }
        }
    }
    catch {
        Write-Host "Python package scan failed: $exe"
    }
}

# 7. Ollama
Add-Section "OLLAMA"

if (Get-Command ollama -ErrorAction SilentlyContinue) {
    $Report.Ollama.Models = Run-Command "ollama" "list"
    $Report.Ollama.RunningModels = Run-Command "ollama" "ps"
    $Report.Ollama.Version = Run-Command "ollama" "--version"

    try {
        $tags = Invoke-RestMethod `
            -Uri "http://localhost:11434/api/tags" `
            -TimeoutSec 5

        $Report.Ollama.APIModels = $tags.models |
            Select-Object name, size, digest
    }
    catch {
        $Report.Ollama.APIStatus = "Local API unavailable"
    }

    $Report.Ollama.Models
    $Report.Ollama.RunningModels
}
else {
    $Report.Ollama.Status = "Not detected"
}

# 8. Installed Windows applications
Add-Section "INSTALLED APPLICATIONS"

$RegistryPaths = @(
    "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
    "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*"
)

$Report.InstalledApplications = @(
    foreach ($path in $RegistryPaths) {
        Get-ItemProperty $path -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName } |
            Select-Object DisplayName, DisplayVersion, Publisher
    }
) | Sort-Object DisplayName -Unique

# 9. Engineering software
Add-Section "ENGINEERING SOFTWARE"

$EngineeringPatterns = @(
    "Abaqus", "ETABS", "SAP2000",
    "SAFE", "STAAD", "AutoCAD",
    "Revit", "Tekla", "ANSYS",
    "SolidWorks", "CATIA",
    "MATLAB", "FreeCAD",
    "Blender", "OpenSees"
)

foreach ($pattern in $EngineeringPatterns) {
    $matches = @(
        $Report.InstalledApplications |
        Where-Object { $_.DisplayName -match $pattern }
    )

    foreach ($app in $matches) {
        $Report.EngineeringSoftware += $app
        Write-Host "$($app.DisplayName) $($app.DisplayVersion)"
    }
}

# 10. Automation tools
Add-Section "AUTOMATION TOOLS"

$AutomationNames = @(
    "playwright", "selenium",
    "autohotkey", "uiautomation",
    "pywinauto", "pyautogui",
    "adb", "wsl", "docker"
)

foreach ($name in $AutomationNames) {
    $Report.AutomationTools += (Check-Command $name)
}

# 11. Browsers
Add-Section "BROWSERS"

$BrowserPaths = @(
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Mozilla Firefox\firefox.exe",
    "$env:LOCALAPPDATA\BraveSoftware\Brave-Browser\Application\brave.exe",
    "$env:LOCALAPPDATA\Microsoft\Edge\Application\msedge.exe"
)

$Report.EnvironmentChecks += @{
    Name = "Browsers"
    Result = @(
        $BrowserPaths |
        Where-Object { Test-Path $_ }
    )
}

# 12. Final report
Add-Section "SAVING REPORT"

$JsonPath = Join-Path $OutDir "Jarvis-PC-Inventory.json"
$TextPath = Join-Path $OutDir "Jarvis-PC-Inventory.txt"

$Report | ConvertTo-Json -Depth 12 |
    Set-Content -Path $JsonPath -Encoding UTF8

$Report | Format-List * |
    Out-String -Width 250 |
    Set-Content -Path $TextPath -Encoding UTF8

Write-Host "`nSCAN COMPLETE" -ForegroundColor Green
Write-Host "JSON: $JsonPath"
Write-Host "TEXT: $TextPath"
Write-Host "`nNo software was installed or modified."
