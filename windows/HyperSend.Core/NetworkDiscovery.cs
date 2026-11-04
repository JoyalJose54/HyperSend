using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace HyperSend.Core
{
    public class LinkHealthInfo
    {
        public string Ssid { get; set; } = "Unknown";
        public string RadioType { get; set; } = "Unknown"; // e.g., 802.11ax
        public string Band { get; set; } = "Unknown"; // e.g., 5 GHz
        public int Channel { get; set; }
        public double ReceiveRateMbps { get; set; }
        public double TransmitRateMbps { get; set; }
        public int SignalPercentage { get; set; }
        public bool IsOptimal5GHzAx => Band.Contains("5 GHz", StringComparison.OrdinalIgnoreCase) && 
                                       (RadioType.Contains("ax", StringComparison.OrdinalIgnoreCase) || TransmitRateMbps >= 400);

        public override string ToString()
        {
            return $"SSID: {Ssid} | Radio: {RadioType} | Band: {Band} | Ch: {Channel} | RX: {ReceiveRateMbps} Mbps | TX: {TransmitRateMbps} Mbps | Signal: {SignalPercentage}%";
        }
    }

    public static class NetworkDiscovery
    {
        /// <summary>
        /// Finds the IPv4 Default Gateway of the active Wi-Fi adapter.
        /// On a phone Mobile Hotspot link, this is precisely the phone's IP address!
        /// </summary>
        public static IPAddress? GetPhoneHotspotGatewayIp()
        {
            foreach (var netInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (netInterface.OperationalStatus != OperationalStatus.Up)
                    continue;

                // Check for wireless interface first
                if (netInterface.NetworkInterfaceType != NetworkInterfaceType.Wireless80211 &&
                    !netInterface.Description.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) &&
                    !netInterface.Description.Contains("Wireless", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var ipProps = netInterface.GetIPProperties();
                foreach (var gateway in ipProps.GatewayAddresses)
                {
                    if (gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !gateway.Address.Equals(IPAddress.Any) &&
                        !gateway.Address.Equals(IPAddress.None))
                    {
                        return gateway.Address;
                    }
                }
            }

            // Fallback: check any active operational interface with a valid IPv4 gateway
            foreach (var netInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (netInterface.OperationalStatus != OperationalStatus.Up)
                    continue;

                var ipProps = netInterface.GetIPProperties();
                foreach (var gateway in ipProps.GatewayAddresses)
                {
                    if (gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !gateway.Address.Equals(IPAddress.Any) &&
                        !gateway.Address.Equals(IPAddress.None))
                    {
                        return gateway.Address;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Detects available transport endpoints in priority order:
        /// 1. USB ADB Reverse (127.0.0.1) if USB connected and adb active.
        /// 2. Mobile Hotspot Gateway IP (e.g. 10.15.70.174).
        /// 3. Fallback to user-specified IP or localhost.
        /// </summary>
        public static (string endpoint, string transportType) ResolvePrioritizedEndpoint(string? manualIp = null)
        {
            if (!string.IsNullOrEmpty(manualIp) && !manualIp.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                return (manualIp, "Manual IP");
            }

            // Check if USB ADB is connected
            if (IsAdbDeviceConnected())
            {
                try
                {
                    // Setup adb reverse tcp:5000 tcp:5000
                    var psi = new ProcessStartInfo
                    {
                        FileName = "adb",
                        Arguments = "reverse tcp:5000 tcp:5000",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(1000);
                    if (p?.ExitCode == 0)
                    {
                        return ("127.0.0.1", "USB 3.0 / ADB High-Speed Link");
                    }
                }
                catch { }
            }

            // Hotspot Gateway
            var gateway = GetPhoneHotspotGatewayIp();
            if (gateway != null)
            {
                return (gateway.ToString(), "5 GHz Mobile Hotspot Direct Link");
            }

            return ("127.0.0.1", "Local Loopback Fallback");
        }

        public static bool IsAdbDeviceConnected()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "adb",
                    Arguments = "devices",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                string outStr = p.StandardOutput.ReadToEnd();
                p.WaitForExit(1000);

                var lines = outStr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith("List of devices") || line.StartsWith("*")) continue;
                    if (line.Contains("device", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Queries Windows WLAN API / netsh to obtain accurate Wi-Fi 6 link health metrics.
        /// </summary>
        public static LinkHealthInfo QueryLinkHealth()
        {
            var info = new LinkHealthInfo();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "wlan show interfaces",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(3000);

                    foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split(':', 2);
                        if (parts.Length < 2) continue;
                        var key = parts[0].Trim();
                        var val = parts[1].Trim();

                        if (key.Equals("SSID", StringComparison.OrdinalIgnoreCase))
                            info.Ssid = val;
                        else if (key.Equals("Radio type", StringComparison.OrdinalIgnoreCase))
                            info.RadioType = val;
                        else if (key.Equals("Band", StringComparison.OrdinalIgnoreCase))
                            info.Band = val;
                        else if (key.Equals("Channel", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out var ch))
                            info.Channel = ch;
                        else if (key.StartsWith("Receive rate", StringComparison.OrdinalIgnoreCase) && double.TryParse(Regex.Match(val, @"[\d.]+").Value, out var rx))
                            info.ReceiveRateMbps = rx;
                        else if (key.StartsWith("Transmit rate", StringComparison.OrdinalIgnoreCase) && double.TryParse(Regex.Match(val, @"[\d.]+").Value, out var tx))
                            info.TransmitRateMbps = tx;
                        else if (key.StartsWith("Signal", StringComparison.OrdinalIgnoreCase) && int.TryParse(Regex.Match(val, @"\d+").Value, out var sig))
                            info.SignalPercentage = sig;
                    }
                }
            }
            catch
            {
                // Silently return partial info on query failure
            }

            return info;
        }
    }
}
