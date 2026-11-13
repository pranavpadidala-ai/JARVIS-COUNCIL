[CmdletBinding()]
param(
    [switch]$StartNow,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
if ($env:OS -ne "Windows_NT") {
    throw "The JARVIS logon task currently requires Windows."
}
Import-Module ScheduledTasks -ErrorAction Stop

$taskName = "JARVIS Background Development Agent"
if ($Uninstall) {
    $existing = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($existing) {
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        Write-Host "Removed the JARVIS background-agent logon task." -ForegroundColor Green
    }
    else {
        Write-Host "No JARVIS background-agent logon task was registered."
    }
    return
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\Council.App\Council.App.csproj"
$startScript = Join-Path $PSScriptRoot "Start-JarvisAgent.ps1"
if (-not (Test-Path $project) -or -not (Test-Path $startScript)) {
    throw "Run this installer from the JARVIS repository; required project or launcher was not found."
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK was not found. Install it before registering the background agent."
}

$provider = if ([string]::IsNullOrWhiteSpace($env:JARVIS_AI_PROVIDER)) { "ollama" } else { $env:JARVIS_AI_PROVIDER.Trim().ToLowerInvariant() }
switch ($provider) {
    "ollama" {
        if (-not (Get-Command ollama -ErrorAction SilentlyContinue)) {
            throw "Ollama is not installed. Run .\scripts\Install-LocalModels.ps1 first."
        }
        $model = if ([string]::IsNullOrWhiteSpace($env:OLLAMA_MODEL)) { "qwen3.5:2b-q4_K_M" } else { $env:OLLAMA_MODEL }
        $installed = (& ollama list 2>$null | Out-String)
        if ($LASTEXITCODE -ne 0 -or $installed -notmatch [regex]::Escape($model)) {
            throw "Local model '$model' is not installed. Run .\scripts\Install-LocalModels.ps1 or set OLLAMA_MODEL to an installed model."
        }
    }
    "gemini" {
        if ([string]::IsNullOrWhiteSpace($env:GEMINI_API_KEY)) { throw "GEMINI_API_KEY must be available to your Windows user before registering this task." }
    }
    "openai" {
        if ([string]::IsNullOrWhiteSpace($env:OPENAI_API_KEY)) { throw "OPENAI_API_KEY must be available to your Windows user before registering this task." }
    }
    "omniroute" {
        if ([string]::IsNullOrWhiteSpace($env:OMNIROUTE_API_KEY) -or
            [string]::IsNullOrWhiteSpace($env:OMNIROUTE_MODEL) -or
            [string]::IsNullOrWhiteSpace($env:OMNIROUTE_FREE_MODELS) -or
            $env:OMNIROUTE_MODEL -eq "auto") {
            throw "OmniRoute requires an API key, an explicit model ID, and its exact approved model allowlist."
        }
    }
    default { throw "Unsupported or non-functional provider '$provider'. Configure Ollama, Gemini, OpenAI or allowlisted OmniRoute." }
}

Write-Host "Building JARVIS before registering the logon task..." -ForegroundColor Cyan
Push-Location $repoRoot
try {
    dotnet build $project --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed. The scheduled task was not registered."
    }
}
finally {
    Pop-Location
}

$userId = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$powerShell = (Get-Command powershell.exe -ErrorAction Stop).Source
$arguments = '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $startScript + '" -NoBuild'

$action = New-ScheduledTaskAction -Execute $powerShell -Argument $arguments -WorkingDirectory $repoRoot
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $userId
$principal = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 5) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description "Runs the local JARVIS development queue at user logon. It executes only one-shot tasks explicitly approved by the user inside JARVIS." -Force | Out-Null

Write-Host "Registered '$taskName' for $userId." -ForegroundColor Green
Write-Host "It runs in the background at logon, uses the current user's provider configuration, and writes to %LOCALAPPDATA%\JARVIS\Agent\agent.log."
Write-Host "Unapproved tasks are never executed. Each background approval is one-shot and can be revoked before it is claimed."
if ($StartNow) {
    Start-ScheduledTask -TaskName $taskName
    Write-Host "Started the JARVIS background agent now." -ForegroundColor Green
}
else {
    Write-Host "It will start at your next Windows logon. Use -StartNow to start it immediately."
}
Write-Host "To remove the task, rerun this script with -Uninstall."
