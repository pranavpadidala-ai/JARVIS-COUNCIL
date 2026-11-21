[CmdletBinding()]
param(
    # Folder with your PDFs (standards, books, papers).
    [Parameter(Mandatory = $true)][string]$Source,
    # Sub-folder name inside the library, e.g. "aluminium" or "concrete".
    [string]$Topic = "general",
    # Library folder JARVIS searches. Default: JARVIS-library next to this repository.
    [string]$Library = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "JARVIS-library"),
    # Where the free pypdf/fontTools packages are kept (outside your normal Python setup).
    [string]$ToolsDir = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "JARVIS-tools\pylib")
)

# Copies the text of your PDFs into JARVIS's private library so it can answer from them
# with document and page references. The PDFs and the library stay on this computer;
# never add them to the Git repository (standards are copyrighted).

$ErrorActionPreference = "Stop"
if (-not (Test-Path $Source)) { throw "PDF folder not found: $Source" }
$python = (Get-Command python -ErrorAction SilentlyContinue).Source
if (-not $python) { throw "Python 3 is required. Install it from python.org, then run this again." }

if (-not (Test-Path (Join-Path $ToolsDir "pypdf"))) {
    Write-Host "Installing the free pypdf and fontTools packages into $ToolsDir ..." -ForegroundColor Cyan
    & $python -m pip install --quiet --disable-pip-version-check --target $ToolsDir pypdf fonttools
    if ($LASTEXITCODE -ne 0) { throw "Could not install pypdf." }
}

$output = Join-Path $Library $Topic
& $python -I (Join-Path $PSScriptRoot "extract_pdf_text.py") $ToolsDir $Source $output 2>$null

# Scanned PDFs have no text layer: read them with Windows' built-in OCR instead (slower, runs one at a time).
foreach ($pdf in Get-ChildItem -Path $Source -Filter *.pdf) {
    $txt = Join-Path $output ($pdf.BaseName + ".txt")
    if (-not (Test-Path $txt)) { continue }
    $content = Get-Content -Raw $txt
    $pages = ([regex]::Matches($content, '(?m)^=== PAGE \d+ ===')).Count
    $letters = ([regex]::Matches($content, '[A-Za-z]')).Count
    if ($pages -gt 0 -and ($letters / $pages) -lt 200) {
        Write-Host "Scanned PDF, reading with Windows OCR (this can take a while): $($pdf.Name)" -ForegroundColor Cyan
        Remove-Item $txt
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Convert-ScannedPdfToText.ps1") -Pdf $pdf.FullName -Output $txt
    }
}
Write-Host ""
Write-Host "Library updated: $output" -ForegroundColor Green
Write-Host "JARVIS picks up the new files automatically. Ask it, for example: 'What does EN 1999-1-1 say about HAZ softening?'"
