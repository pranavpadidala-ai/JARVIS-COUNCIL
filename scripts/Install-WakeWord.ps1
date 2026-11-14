[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9]+(?:[ ]+[A-Za-z0-9]+){1,3}$')]
    [string]$WakePhrase = "HEY JARVIS"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$archiveUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/kws-models/sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01.tar.bz2"
$modelName = "sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01"
$localRoot = Join-Path $env:LOCALAPPDATA "JARVIS"
$modelRoot = Join-Path (Join-Path $localRoot "models") $modelName
$toolsVenv = Join-Path $localRoot "wakeword-tools"
$toolsPython = Join-Path $toolsVenv "Scripts\python.exe"
$helper = Join-Path $PSScriptRoot "Generate-WakeWordTokens.py"

if (-not $IsWindows -and $PSVersionTable.PSVersion.Major -ge 6) {
    throw "The offline wake-word pack currently supports Windows only."
}
if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
    throw "Python 3 is required to generate the model's BPE keyword file. Install Python 3.10+ and run this script again."
}
if (-not (Get-Command tar.exe -ErrorAction SilentlyContinue)) {
    throw "Windows tar.exe was not found. Install the Windows tar utility before continuing."
}
if (-not (Test-Path $helper -PathType Leaf)) {
    throw "The keyword-token helper is missing: $helper"
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

$alreadyInstalled = $true
foreach ($file in $requiredFiles) {
    if (-not (Test-Path (Join-Path $modelRoot $file) -PathType Leaf)) {
        $alreadyInstalled = $false
        break
    }
}

if (-not $alreadyInstalled) {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("jarvis-wakeword-" + [guid]::NewGuid().ToString("N"))
    $archivePath = Join-Path $tempRoot "wakeword-model.tar.bz2"
    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

    try {
        Write-Host "Downloading the English sherpa-onnx wake-word model (~19 MB)..."
        Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath -UseBasicParsing
        if ((Get-Item $archivePath).Length -lt 5MB) {
            throw "The downloaded model archive is unexpectedly small. Nothing was installed."
        }

        & tar.exe -xjf $archivePath -C $tempRoot
        if ($LASTEXITCODE -ne 0) {
            throw "Windows tar.exe could not extract the wake-word archive."
        }

        $extracted = Join-Path $tempRoot $modelName
        if (-not (Test-Path $extracted -PathType Container)) {
            throw "The downloaded archive did not contain the expected model folder."
        }

        $modelReadme = Join-Path $extracted "README.md"
        if (-not (Test-Path $modelReadme -PathType Leaf) -or
            -not (Select-String -Path $modelReadme -Pattern "Apache License 2.0" -Quiet)) {
            throw "The model README did not declare Apache License 2.0. Installation was stopped for licence review."
        }

        foreach ($file in $requiredFiles) {
            $source = Join-Path $extracted $file
            if (-not (Test-Path $source -PathType Leaf) -or (Get-Item $source).Length -eq 0) {
                throw "Required model asset is missing or empty: $file"
            }
        }

        New-Item -ItemType Directory -Force -Path $modelRoot | Out-Null
        foreach ($file in $requiredFiles) {
            Copy-Item -LiteralPath (Join-Path $extracted $file) -Destination (Join-Path $modelRoot $file) -Force
        }
        Write-Host "Model assets installed locally: $modelRoot"
    }
    finally {
        if (Test-Path $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
else {
    Write-Host "The local wake-word model is already installed."
}

if (-not (Test-Path $toolsPython -PathType Leaf)) {
    Write-Host "Creating the small local Python keyword-token utility environment..."
    & python -m venv $toolsVenv
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create the local Python environment for keyword token generation."
    }
}

Write-Host "Checking the local SentencePiece utility..."
& $toolsPython -m pip install --disable-pip-version-check --quiet "sentencepiece==0.2.1"
if ($LASTEXITCODE -ne 0) {
    throw "Could not install SentencePiece. The wake-word model remains installed, but keywords were not generated."
}

$keywordPath = Join-Path $modelRoot "keywords.txt"
Write-Host "Generating the keyword file for '$WakePhrase'..."
$helperArguments = @(
    $helper,
    "--phrase", $WakePhrase,
    "--tokens", (Join-Path $modelRoot "tokens.txt"),
    "--bpe-model", (Join-Path $modelRoot "bpe.model"),
    "--output", $keywordPath
)
& $toolsPython @helperArguments
if ($LASTEXITCODE -ne 0) {
    throw "Keyword generation failed. Review the message above and try a different phrase."
}

Write-Host ""
Write-Host "Wake-word pack is ready."
Write-Host "Phrase : $WakePhrase"
Write-Host "Model  : $modelRoot"
Write-Host "Privacy: audio is processed locally; no recording is saved or uploaded by the detector."
Write-Host "To use it, open JARVIS and enable 'Listen for Hey JARVIS' in the system tray."
