# HyperSend Windows Firewall Setup (PowerShell)
# Configures Windows Defender Firewall to allow inbound high-speed transfers on TCP port 5000.

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Warning "Administrative permissions required. Relaunching as Administrator..."
    Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit
}

Write-Host "Configuring Windows Defender Firewall for HyperSend..." -ForegroundColor Cyan

try {
    Remove-NetFirewallRule -DisplayName "HyperSend Direct Transfer" -ErrorAction SilentlyContinue
    New-NetFirewallRule -DisplayName "HyperSend Direct Transfer" `
                        -Direction Inbound `
                        -Action Allow `
                        -Protocol TCP `
                        -LocalPort 5000 `
                        -Profile Any `
                        -Description "Allows incoming P2P file transfers over Wi-Fi 6 hotspot and USB links for HyperSend."
    Write-Host "[SUCCESS] HyperSend firewall rule installed successfully!" -ForegroundColor Green
} catch {
    Write-Error "Failed to install firewall rule: $_"
}

Read-Host -Prompt "Press Enter to exit"

