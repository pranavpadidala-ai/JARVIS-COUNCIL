# Install the RAM-conscious local JARVIS model stack for an 8 GB Windows laptop.
# Run from the repository root:
#   Set-ExecutionPolicy -Scope Process Bypass
#   .\scripts\Install-LocalModels.ps1

[CmdletBinding()]
param(
    # Where Whisper, Piper voices and the Piper Python runtime are stored.
    # Defaults to Models\LocalAI inside this repository (the large E: drive on the owner's laptop).
    [string]$ModelsRoot
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$root = if (-not [string]::IsNullOrWhiteSpace($ModelsRoot)) { $ModelsRoot }
        elseif (-not [string]::IsNullOrWhiteSpace($env:JARVIS_MODELS_ROOT)) { $env:JARVIS_MODELS_ROOT }
        else { Join-Path $repoRoot "Models\LocalAI" }
# Tell the JARVIS app where the models are, now and after the next sign-in.
[Environment]::SetEnvironmentVariable("JARVIS_MODELS_ROOT", $root, "User")
$env:JARVIS_MODELS_ROOT = $root
Write-Host "Local models folder: $root"
$models = Join-Path $root "models"
$voices = Join-Path $root "voices"
$pythonEnv = Join-Path $root "piper-venv"
New-Item -ItemType Directory -Force -Path $models, $voices | Out-Null

$ollama = Get-Command ollama -ErrorAction SilentlyContinue
if (-not $ollama) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw "Ollama is not installed and winget is unavailable. Install Ollama from https://ollama.com/download, then rerun this script."
    }
    winget install --id Ollama.Ollama -e --accept-package-agreements --accept-source-agreements
    $ollama = Get-Command ollama -ErrorAction SilentlyContinue
    if (-not $ollama) {
        throw "Ollama was installed. Close and reopen PowerShell, then rerun this script."
    }
}

$versionText = (& $ollama.Source --version 2>&1) -join " "
$versionMatch = [regex]::Match($versionText, "(\d+)\.(\d+)\.(\d+)")
if (-not $versionMatch.Success) { throw "Could not determine Ollama version. Update to 0.12.7 or newer." }
$installedVersion = [version]::new([int]$versionMatch.Groups[1].Value, [int]$versionMatch.Groups[2].Value, [int]$versionMatch.Groups[3].Value)
if ($installedVersion -lt [version]"0.12.7") { throw "Qwen3-VL requires Ollama 0.12.7 or newer. Update Ollama from https://ollama.com/download." }

$ollamaSettings = @{
    OLLAMA_MAX_LOADED_MODELS = "1"
    OLLAMA_NUM_PARALLEL = "1"
    OLLAMA_FLASH_ATTENTION = "1"
    OLLAMA_KV_CACHE_TYPE = "q8_0"
    OLLAMA_KEEP_ALIVE = "30s"
}
foreach ($entry in $ollamaSettings.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "User")
    Set-Item -Path ("Env:" + $entry.Key) -Value $entry.Value
}
$env:JARVIS_OLLAMA_KEEP_ALIVE = "30s"
$env:JARVIS_OLLAMA_VISION_KEEP_ALIVE = "0"
$env:JARVIS_OLLAMA_CONTEXT_TOKENS = "4096"
[Environment]::SetEnvironmentVariable("JARVIS_OLLAMA_VISION_KEEP_ALIVE", "0", "User")

Write-Host "Pulling Qwen 3.5 2B Q4_K_M local brain..."
& $ollama.Source pull "qwen3.5:2b-q4_K_M"
if ($LASTEXITCODE -ne 0) { throw "Could not pull the Qwen brain model." }

Write-Host "Pulling Qwen3-VL 2B desktop/computer vision model..."
& $ollama.Source pull "qwen3-vl:2b"
if ($LASTEXITCODE -ne 0) { throw "Could not pull the Qwen3-VL vision model." }

Write-Host "Pulling GLM-OCR specialist engineering-document model..."
& $ollama.Source pull "glm-ocr"
if ($LASTEXITCODE -ne 0) { throw "Could not pull the GLM-OCR document model." }

$whisperPath = Join-Path $models "ggml-small.bin"
if (-not (Test-Path $whisperPath) -or (Get-Item $whisperPath).Length -lt 400MB) {
    Write-Host "Downloading Whisper small multilingual model (~466 MB)..."
    Invoke-WebRequest -Uri "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin?download=true" -OutFile $whisperPath
    if ((Get-Item $whisperPath).Length -lt 400MB) { throw "Whisper model download appears incomplete." }
}

$pythonLauncher = Get-Command py -ErrorAction SilentlyContinue
if (-not $pythonLauncher) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw "Python 3.11 is required for Piper. Install Python 3.11, then rerun this script."
    }
    winget install --id Python.Python.3.11 -e --accept-package-agreements --accept-source-agreements
    $pythonLauncher = Get-Command py -ErrorAction SilentlyContinue
    if (-not $pythonLauncher) { throw "Python was installed. Reopen PowerShell and rerun this script." }
}

if (-not (Test-Path (Join-Path $pythonEnv "Scripts\python.exe"))) {
    & $pythonLauncher.Source -3.11 -m venv $pythonEnv
    if ($LASTEXITCODE -ne 0) { throw "Could not create the Piper Python environment." }
}
$piperPython = Join-Path $pythonEnv "Scripts\python.exe"
Write-Host "Installing Piper neural speech runtime..."
& $piperPython -m pip install --upgrade pip
if ($LASTEXITCODE -ne 0) { throw "Could not update pip." }
& $piperPython -m pip install "piper-tts==1.8.0"
if ($LASTEXITCODE -ne 0) { throw "Could not install Piper TTS." }

Write-Host "Downloading medium-quality English, Telugu and Hindi neural voices..."
& $piperPython -m piper.download_voices --data-dir $voices en_US-lessac-medium te_IN-maya-medium hi_IN-pratham-medium
if ($LASTEXITCODE -ne 0) { throw "Could not download one or more Piper voices." }

[Environment]::SetEnvironmentVariable("JARVIS_PIPER_PYTHON", $piperPython, "User")
[Environment]::SetEnvironmentVariable("JARVIS_PIPER_VOICES", $voices, "User")
[Environment]::SetEnvironmentVariable("JARVIS_WHISPER_MODEL_PATH", $whisperPath, "User")
[Environment]::SetEnvironmentVariable("JARVIS_LOCAL_WHISPER_ENABLED", "true", "User")
[Environment]::SetEnvironmentVariable("JARVIS_PIPER_VOICE", "en_US-lessac-medium", "User")

Write-Host ""
Write-Host "Local JARVIS model stack installed."
Write-Host "Brain : qwen3.5:2b-q4_K_M (Ollama)"
Write-Host "Screen eyes  : qwen3-vl:2b (Ollama)"
Write-Host "Document OCR : glm-ocr (Ollama)"
Write-Host "Ears  : Whisper small multilingual (CPU, loaded only for /voice)"
Write-Host "Mouth : Piper medium English + Telugu + Hindi voices"
Write-Host "Storage: $root"
Write-Host ""
Write-Host "Close and reopen Ollama so its one-model/one-parallel/Q8-KV memory settings take effect."
Write-Host "Vision models unload after each request by default to preserve RAM; change JARVIS_OLLAMA_VISION_KEEP_ALIVE only if you have spare memory."
Write-Host "Then set JARVIS_AI_PROVIDER=ollama to use the local brain, or keep Gemini/OpenAI as your cloud brain."
