$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git is required to initialize the pinned external integrations.'
}

Write-Host 'Synchronizing pinned JARVIS integrations...'
git submodule sync --recursive
git submodule update --init --recursive
if ($LASTEXITCODE -ne 0) { throw 'Git submodule initialization failed.' }

$trader = Join-Path $repoRoot 'integrations\indian-trader'
if (Test-Path (Join-Path $trader 'it.py')) {
    $python = Get-Command python -ErrorAction SilentlyContinue
    if (-not $python) {
        Write-Warning 'Python 3.8+ was not found. Indian Trader source is present, but its JARVIS tool will remain unavailable until Python is installed.'
    } else {
        $versionText = & python --version 2>&1 | Out-String
        Write-Host "Python: $versionText"
        $versionMatch = [regex]::Match($versionText, 'Python\s+(\d+)\.(\d+)')
        if (-not $versionMatch.Success -or [int]$versionMatch.Groups[1].Value -lt 3 -or
            ([int]$versionMatch.Groups[1].Value -eq 3 -and [int]$versionMatch.Groups[2].Value -lt 8)) {
            throw 'Indian Trader requires Python 3.8 or newer.'
        }
        Write-Host 'Running Indian Trader offline test suite...'
        Push-Location $trader
        try {
            & python -m unittest discover -s tests -v
            if ($LASTEXITCODE -ne 0) { throw 'Indian Trader upstream tests failed.' }
        } finally { Pop-Location }
    }
}

Write-Host ''
Write-Host 'External sources are initialized.'
Write-Host 'OpenJarvis is pinned for selective source evaluation; this script does not install Python packages, compile Rust/MSVC extensions, download models, or start background services.'
Write-Host 'Indian Trader is exposed through JARVIS only via its read-only CLI allowlist.'
