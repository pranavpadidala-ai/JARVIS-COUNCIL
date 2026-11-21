[CmdletBinding()]
param(
    # A scanned PDF (no text layer), e.g. an old Indian Standard, or an image (PNG, JPG, BMP, TIFF, GIF).
    [Parameter(Mandatory = $true)][string]$Pdf,
    # Output .txt for the JARVIS library. Default: next to the PDF with the same name.
    [string]$Output,
    # Render width in pixels. Higher reads small print better but is slower.
    [int]$Width = 2000,
    # Short pause between pages so a small laptop stays cool.
    [int]$PauseMilliseconds = 300,
    # Stop after this many pages (0 = all). JARVIS chat uses a limit so a reply never waits for a whole book.
    [int]$MaxPages = 0
)

# OCR with the engine built into Windows 10/11 (free, offline, no download).
# Writes "=== PAGE n ===" sections that JARVIS's standards_library search understands.
# Safe to stop and re-run: finished pages are kept and it continues where it stopped.
# Must run in Windows PowerShell 5.1 (powershell.exe), which can call Windows Runtime APIs.

$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSEdition -ne "Desktop") {
    throw "Run this with Windows PowerShell 5.1 (powershell.exe), not PowerShell 7."
}
$Pdf = (Resolve-Path $Pdf).Path
if (-not $Output) { $Output = [IO.Path]::ChangeExtension($Pdf, ".txt") }

# Run quietly in the background so the laptop stays usable.
try { (Get-Process -Id $PID).PriorityClass = "BelowNormal" } catch { }

Add-Type -AssemblyName System.Runtime.WindowsRuntime
[void][Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
[void][Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType = WindowsRuntime]
[void][Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
[void][Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics, ContentType = WindowsRuntime]
[void][Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]

$asTaskOperation = [System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object { $_.Name -eq "AsTask" -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } |
    Select-Object -First 1
$asTaskAction = [System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object { $_.Name -eq "AsTask" -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq "IAsyncAction" } |
    Select-Object -First 1

function Wait-Operation($operation, [Type]$resultType) {
    $task = $asTaskOperation.MakeGenericMethod($resultType).Invoke($null, @($operation))
    [void]$task.Wait(-1)
    return $task.Result
}
function Wait-Action($action) {
    $task = $asTaskAction.Invoke($null, @($action))
    [void]$task.Wait(-1)
}

$engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
if ($null -eq $engine) { throw "Windows OCR is not available for your display language. Add English (United States) in Windows language settings." }

$file = Wait-Operation ([Windows.Storage.StorageFile]::GetFileFromPathAsync($Pdf)) ([Windows.Storage.StorageFile])

# A photo or screenshot: one page, no rendering needed.
if ([IO.Path]::GetExtension($Pdf).ToLowerInvariant() -in @(".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".gif")) {
    $imageStream = Wait-Operation ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
    try {
        $decoder = Wait-Operation ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($imageStream)) ([Windows.Graphics.Imaging.BitmapDecoder])
        $bitmap = Wait-Operation ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
        if ($bitmap.PixelWidth -gt [Windows.Media.Ocr.OcrEngine]::MaxImageDimension -or $bitmap.PixelHeight -gt [Windows.Media.Ocr.OcrEngine]::MaxImageDimension) {
            throw "The image is larger than Windows OCR accepts ($([Windows.Media.Ocr.OcrEngine]::MaxImageDimension) px per side). Resize it first."
        }
        $result = Wait-Operation ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
        $text = ($result.Lines | ForEach-Object { $_.Text }) -join "`n"
        [IO.File]::WriteAllText($Output, "`n=== PAGE 1 ===`n$text`n", [Text.Encoding]::UTF8)
        $bitmap.Dispose()
    }
    finally { $imageStream.Dispose() }
    Write-Host "Done: $Output" -ForegroundColor Green
    return
}

$document = Wait-Operation ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
$pageCount = [int]$document.PageCount
$totalPages = $pageCount
if ($MaxPages -gt 0 -and $MaxPages -lt $pageCount) { $pageCount = $MaxPages }

# Resume: find the last complete page already written.
$done = 0
if (Test-Path $Output) {
    $markers = Select-String -Path $Output -Pattern '^=== PAGE (\d+) ===' -AllMatches
    if ($markers) { $done = [int]($markers[-1].Matches[0].Groups[1].Value) - 1 } # redo the last page in case it was cut off
    if ($done -gt 0) {
        $kept = New-Object System.Collections.Generic.List[string]
        foreach ($line in [IO.File]::ReadAllLines($Output)) {
            if ($line -match '^=== PAGE (\d+) ===' -and [int]$Matches[1] -gt $done) { break }
            $kept.Add($line)
        }
        [IO.File]::WriteAllLines($Output, $kept)
    } else {
        Remove-Item $Output
    }
}

$options = New-Object Windows.Data.Pdf.PdfPageRenderOptions
$options.DestinationWidth = [uint32]$Width
$started = Get-Date

for ($index = $done; $index -lt $pageCount; $index++) {
    $page = $document.GetPage([uint32]$index)
    $stream = New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
    try {
        Wait-Action ($page.RenderToStreamAsync($stream, $options))
        $decoder = Wait-Operation ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
        $bitmap = Wait-Operation ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
        $result = Wait-Operation ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
        $text = ($result.Lines | ForEach-Object { $_.Text }) -join "`n"
        [IO.File]::AppendAllText($Output, "`n=== PAGE $($index + 1) ===`n$text`n", [Text.Encoding]::UTF8)
        $bitmap.Dispose()
    }
    finally {
        $stream.Dispose()
        $page.Dispose()
    }

    $finished = $index + 1
    if ($finished % 10 -eq 0 -or $finished -eq $pageCount) {
        $perPage = ((Get-Date) - $started).TotalSeconds / ($finished - $done)
        $left = [TimeSpan]::FromSeconds($perPage * ($pageCount - $finished))
        Write-Host ("{0}: page {1} of {2}, about {3:mm\:ss} left" -f (Split-Path $Pdf -Leaf), $finished, $pageCount, $left)
    }
    if ($PauseMilliseconds -gt 0) { Start-Sleep -Milliseconds $PauseMilliseconds }
}

if ($pageCount -lt $totalPages) {
    Write-Host "Read the first $pageCount of $totalPages pages (page limit). Run again with -MaxPages 0 for the rest."
}
Write-Host "Done: $Output" -ForegroundColor Green
