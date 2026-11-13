[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 47821,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
if ($env:OS -ne "Windows_NT") {
    throw "JARVIS phone-bridge pairing currently requires Windows."
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell window. The JARVIS app itself runs as your normal user."
}

$variables = @("JARVIS_BRIDGE_BIND", "JARVIS_BRIDGE_PORT", "JARVIS_BRIDGE_TOKEN")
if ($Uninstall) {
    & (Join-Path $PSScriptRoot "Install-JarvisPhoneBridge.ps1") -Port $Port -Uninstall
    foreach ($name in $variables) {
        [Environment]::SetEnvironmentVariable($name, $null, "User")
        Remove-Item ("Env:" + $name) -ErrorAction SilentlyContinue
    }
    Write-Host "Removed JARVIS phone-bridge settings. Reopen JARVIS to apply the change." -ForegroundColor Green
    return
}

$tailscale = Get-Command tailscale -ErrorAction SilentlyContinue
if (-not $tailscale) {
    throw "Tailscale CLI was not found. Install Tailscale on this laptop, sign in, reopen PowerShell, and rerun this script."
}

$ipOutput = & $tailscale.Source ip -4
if ($LASTEXITCODE -ne 0) {
    throw "Could not obtain the laptop's Tailscale IPv4 address. Check that Tailscale is connected."
}
$bindAddress = ($ipOutput | Select-Object -First 1).ToString().Trim()
$parsedAddress = $null
if (-not [System.Net.IPAddress]::TryParse($bindAddress, [ref]$parsedAddress)) {
    throw "Tailscale returned an invalid IPv4 address."
}
$bytes = $parsedAddress.GetAddressBytes()
if ($parsedAddress.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork -or
    $bytes[0] -ne 100 -or $bytes[1] -lt 64 -or $bytes[1] -gt 127) {
    throw "Tailscale returned an address outside 100.64.0.0/10. The bridge refuses to bind to public or ordinary LAN addresses."
}

$random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$tokenBytes = New-Object byte[] 32
try {
    $random.GetBytes($tokenBytes)
}
finally {
    $random.Dispose()
}
$token = [Convert]::ToBase64String($tokenBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
[Array]::Clear($tokenBytes, 0, $tokenBytes.Length)

& (Join-Path $PSScriptRoot "Install-JarvisPhoneBridge.ps1") -Port $Port
if ($LASTEXITCODE -ne 0) {
    throw "Could not configure the Windows URL reservation and Tailscale-only firewall rule."
}

$settings = @{
    JARVIS_BRIDGE_BIND = $bindAddress
    JARVIS_BRIDGE_PORT = $Port.ToString()
    JARVIS_BRIDGE_TOKEN = $token
}
foreach ($entry in $settings.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "User")
    Set-Item -Path ("Env:" + $entry.Key) -Value $entry.Value
}

Write-Host ""
Write-Host "JARVIS phone bridge paired on this laptop." -ForegroundColor Green
Write-Host "Laptop Tailscale IPv4 : $bindAddress"
Write-Host "Port                  : $Port"
Write-Host "Bridge token (copy once into JARVIS Phone):" -ForegroundColor Yellow
Write-Host $token -ForegroundColor Yellow
Write-Host ""
Write-Host "The token is saved in your Windows user environment and is available to apps running as your Windows account."
Write-Host "Close and reopen JARVIS Command Center so it starts the phone bridge."
Write-Host "Keep both devices connected to the same Tailscale network. Never expose this port through router forwarding."
Write-Host "To disable the bridge later, run this script with -Uninstall from an elevated PowerShell window."
