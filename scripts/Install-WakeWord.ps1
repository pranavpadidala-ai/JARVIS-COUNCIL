[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9]+(?:[ ]+[A-Za-z0-9]+){1,3}$')]
    [string]$WakePhrase = "HEY JARVIS"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$repoRoot = Split-Path -Parent $PSScriptRoot
$modelName = "sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01"
$modelRoot = Join-Path $repoRoot "Models\LocalAI\WakeWord\$modelName"
$toolsVenv = Join-Path $repoRoot "Models\LocalAI\wakeword-venv"
$toolsPython = Join-Path $toolsVenv "Scripts\python.exe"
$helper = Join-Path $PSScriptRoot "Generate-WakeWordTokens.py"
$archiveUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/kws-models/sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01.tar.bz2"

$pythonLauncher = Get-Command py.exe -ErrorAction SilentlyContinue
if (-not $pythonLauncher) {
    throw "The Python launcher py.exe was not found. JARVIS requires Python 3.11 for this optional feature."
}
& $pythonLauncher.Source -3.11 --version
if ($LASTEXITCODE -ne 0) { throw "Python 3.11 is required for wake-word installation." }
if (-not (Get-Command tar.exe -ErrorAction SilentlyContinue)) {
    throw "Windows tar.exe is required to extract the model."
}
if (-not (Test-Path $helper -PathType Leaf)) {
    throw "The Python keyword-token helper is missing: $helper"
}

New-Item -ItemType Directory -Force -Path (Split-Path $modelRoot -Parent) | Out-Null
$requiredFiles = @(
    "encoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx",
    "decoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx",
    "joiner-epoch-12-avg-2-chunk-16-left-64.int8.onnx",
    "tokens.txt",
    "bpe.model",
    "README.md"
)

$installed = $true
foreach ($file in $requiredFiles) {
    $path = Join-Path $modelRoot $file
    if (-not (Test-Path $path -PathType Leaf) -or (Get-Item $path).Length -eq 0) {
        $installed = $false
        break
    }
}

if (-not $installed) {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("jarvis-wakeword-" + [guid]::NewGuid().ToString("N"))
    $archivePath = Join-Path $tempRoot "model.tar.bz2"
    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    try {
        Write-Host "Downloading the small English CPU wake-word model..."
        Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath -UseBasicParsing
        if ((Get-Item $archivePath).Length -lt 5MB) {
            throw "The model archive is unexpectedly small; installation stopped."
        }

        & tar.exe -xjf $archivePath -C $tempRoot
        if ($LASTEXITCODE -ne 0) { throw "Model archive extraction failed." }

        $extracted = Join-Path $tempRoot $modelName
        $licenseReadme = Join-Path $extracted "README.md"
        if (-not (Test-Path $licenseReadme) -or
            -not (Select-String -Path $licenseReadme -Pattern "Apache License 2.0" -Quiet)) {
            throw "The model README did not declare Apache License 2.0. Nothing was installed."
        }

        foreach ($file in $requiredFiles) {
            $source = Join-Path $extracted $file
            if (-not (Test-Path $source -PathType Leaf) -or (Get-Item $source).Length -eq 0) {
                throw "Required model asset missing or empty: $file"
            }
        }

        New-Item -ItemType Directory -Force -Path $modelRoot | Out-Null
        foreach ($file in $requiredFiles) {
            Copy-Item -LiteralPath (Join-Path $extracted $file) -Destination (Join-Path $modelRoot $file) -Force
        }
    }
    finally {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-Path $toolsPython -PathType Leaf)) {
    Write-Host "Creating a dedicated, lightweight Python wake-word environment on the JARVIS drive..."
    & $pythonLauncher.Source -3.11 -m venv $toolsVenv
    if ($LASTEXITCODE -ne 0) { throw "Could not create the wake-word Python environment." }
}

Write-Host "Installing CPU-only local wake-word dependencies..."
& $toolsPython -m pip install --disable-pip-version-check --quiet "sherpa-onnx==1.13.8" "sounddevice>=0.5,<0.6" "sentencepiece==0.2.1"
if ($LASTEXITCODE -ne 0) { throw "Could not install the local wake-word dependencies." }

$keywordPath = Join-Path $modelRoot "keywords.txt"
Write-Host "Generating keyword tokens for '$WakePhrase'..."
& $toolsPython $helper --phrase $WakePhrase --tokens (Join-Path $modelRoot "tokens.txt") --bpe-model (Join-Path $modelRoot "bpe.model") --output $keywordPath
if ($LASTEXITCODE -ne 0) { throw "Wake-word token generation failed." }

[Environment]::SetEnvironmentVariable("JARVIS_WAKEWORD_MODEL_DIR", $modelRoot, "User")
[Environment]::SetEnvironmentVariable("JARVIS_WAKEWORD_PYTHON", $toolsPython, "User")
$env:JARVIS_WAKEWORD_MODEL_DIR = $modelRoot
$env:JARVIS_WAKEWORD_PYTHON = $toolsPython

Write-Host ""
Write-Host "Wake-word model and Python runtime installed under Models\LocalAI."
Write-Host "Phrase: $WakePhrase"
Write-Host "Audio remains local. Listening stays OFF until you enable it in the JARVIS tray menu."
Write-Host "Restart JARVIS Command Center after installation so it reads the new user environment variables."
