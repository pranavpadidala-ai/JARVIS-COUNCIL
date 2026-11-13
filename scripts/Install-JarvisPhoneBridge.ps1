[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 47821,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
if ($env:OS -ne "Windows_NT") {
    throw "The JARVIS phone bridge setup currently requires Windows."
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script once from an elevated PowerShell window. The JARVIS app itself will still run as your normal user."
}

$prefix = "http://+:$Port/"
$ruleName = "JARVIS Phone Bridge - Tailscale Only"

if ($Uninstall) {
    & netsh http delete urlacl "url=$prefix" | Out-Host
    Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    Write-Host "Removed the JARVIS phone-bridge URL reservation and firewall rule." -ForegroundColor Green
    return
}

$userId = $identity.Name
& netsh http add urlacl "url=$prefix" "user=$userId" | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Could not reserve $prefix for $userId. Check whether another account already owns this URL reservation."
}

Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName $ruleName -Description "Allows the JARVIS phone inbox only between Tailscale IPv4 peers. The application also binds exclusively to the laptop's Tailscale IPv4 address and requires a 32-byte bearer token." -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -LocalAddress "100.64.0.0/10" -RemoteAddress "100.64.0.0/10" -Profile Any | Out-Null

Write-Host "Configured the JARVIS phone bridge for port $Port." -ForegroundColor Green
Write-Host "The app still binds only to the specific JARVIS_BRIDGE_BIND Tailscale IPv4 address and authenticates every request with JARVIS_BRIDGE_TOKEN."
Write-Host "Do not expose this port through router port-forwarding or public reverse proxies."
