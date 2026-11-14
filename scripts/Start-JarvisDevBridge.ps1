[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 47822
)

$ErrorActionPreference = "Stop"
if ($env:OS -ne "Windows_NT") {
    throw "The JARVIS desktop development bridge currently targets Windows."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\Council.Bridge\Council.Bridge.csproj"
if (-not (Test-Path -LiteralPath $project)) {
    throw "Bridge project not found. Pull the latest JARVIS integration branch first."
}

$random = [Security.Cryptography.RandomNumberGenerator]::Create()
$bytes = New-Object byte[] 32
try {
    $random.GetBytes($bytes)
}
finally {
    $random.Dispose()
}
$token = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
[Array]::Clear($bytes, 0, $bytes.Length)

$env:JARVIS_DEV_BRIDGE_TOKEN = $token
$env:JARVIS_WORKSPACE_ROOT = $repoRoot
$env:JARVIS_DEV_BRIDGE_PORT = $Port.ToString()

Write-Host ""
Write-Host "JARVIS local development bridge" -ForegroundColor Cyan
Write-Host "MCP endpoint : http://127.0.0.1:$Port/mcp"
Write-Host "Workspace    : $repoRoot"
Write-Host "Access token (keep private; shown once for local MCP client setup):" -ForegroundColor Yellow
Write-Host $token -ForegroundColor Yellow
Write-Host ""
Write-Host "Security: loopback-only listener; fixed Git/.NET commands; read-only workspace files."
Write-Host "Development tasks are queued but are not approved or executed by this bridge."
Write-Host "This endpoint is not reachable from ChatGPT's cloud until a separate authenticated remote connector/relay is configured."
Write-Host "Press Ctrl+C to stop the bridge."
Write-Host ""

Push-Location $repoRoot
try {
    dotnet run --project $project --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "JARVIS development bridge exited with code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
    Remove-Item Env:JARVIS_DEV_BRIDGE_TOKEN -ErrorAction SilentlyContinue
    Remove-Item Env:JARVIS_WORKSPACE_ROOT -ErrorAction SilentlyContinue
    Remove-Item Env:JARVIS_DEV_BRIDGE_PORT -ErrorAction SilentlyContinue
}
