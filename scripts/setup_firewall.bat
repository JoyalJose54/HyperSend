@echo off
:: HyperSend Windows Firewall Setup
:: Configures Windows Defender Firewall to allow inbound high-speed transfers on TCP port 5000.

echo ========================================================
echo  HyperSend Windows Firewall Configuration
echo ========================================================

net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] Administrative permissions required.
    echo Please right-click this script and select "Run as administrator".
    pause
    exit /b 1
)

echo Adding inbound rule for HyperSend (TCP 5000)...
netsh advfirewall firewall delete rule name="HyperSend Direct Transfer" >nul 2>&1
netsh advfirewall firewall add rule name="HyperSend Direct Transfer" dir=in action=allow protocol=TCP localport=5000 profile=any

if %errorLevel% equ 0 (
    echo [SUCCESS] Firewall rule successfully added!
    echo Sockets on TCP port 5000 are now accessible over Wi-Fi Hotspot and USB links.
) else (
    echo [FAILED] Failed to add firewall rule. Error code: %errorLevel%
)

pause

