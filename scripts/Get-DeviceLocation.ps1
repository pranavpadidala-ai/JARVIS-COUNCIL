<#
.SYNOPSIS
  Prints this laptop's location as one JSON line, using the Windows location service.
  Needs Windows Settings > Privacy & security > Location turned on. Runs in Windows PowerShell 5.1.
#>
param([int]$TimeoutSeconds = 10)
$ErrorActionPreference = 'Stop'
try {
    Add-Type -AssemblyName System.Device
    $watcher = New-Object System.Device.Location.GeoCoordinateWatcher([System.Device.Location.GeoPositionAccuracy]::Default)
    $null = $watcher.TryStart($true, [TimeSpan]::FromSeconds($TimeoutSeconds))
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($watcher.Position.Location.IsUnknown -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    $location = $watcher.Position.Location
    if ($watcher.Permission -eq 'Denied') {
        '{"ok":false,"error":"denied"}'
    } elseif ($location.IsUnknown) {
        '{"ok":false,"error":"nodata"}'
    } else {
        [pscustomobject]@{ ok = $true; lat = $location.Latitude; lon = $location.Longitude; accuracy_m = [int]$location.HorizontalAccuracy } |
            ConvertTo-Json -Compress
    }
    $watcher.Stop()
} catch {
    [pscustomobject]@{ ok = $false; error = $_.Exception.Message } | ConvertTo-Json -Compress
}
