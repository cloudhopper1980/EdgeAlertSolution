using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using EdgeAlertSignalClient.Services;
using EdgeAlertSignalClient.Stores;
using EdgeAlertSignalClient.Utilities;
using Microsoft.Extensions.Logging;

namespace EdgeAlertSignalClient.Handlers
{
    public static class IPAddressHandler
    {
        private static CommandLineArgsHolder _argsHolder;
        private static NetworkHandler _networkHandler;
        private static JSONHandler _jsonHandler;
        private static ILogger _log;

        public static void Initialize(
            CommandLineArgsHolder argsHolder,
            NetworkHandler networkHandler,
            JSONHandler jsonHandler,
            ILogger log)
        {
            _argsHolder = argsHolder;
            _networkHandler = networkHandler;
            _jsonHandler = jsonHandler;
            _log = log;
        }

        // Modified to return first trusted IP, or null if no trusted IPs are found
        public static IPAddress GetTrustedIPAddress()
        {
            // Check command line args first, unconditionally
            if (_argsHolder?.Args != null && _argsHolder.Args.Length > 2 && !string.IsNullOrEmpty(_argsHolder.Args[2]))
            {
                _log?.LogInformation($"GetTrustedIPAddress: Checking args[2] for IP override: {_argsHolder.Args[2]}");
                if (IPAddress.TryParse(_argsHolder.Args[2], out IPAddress customIP) && IsTrustedIP(customIP))
                {
                    _log?.LogInformation($"GetTrustedIPAddress: Using custom trusted IP from arguments: {customIP}");
                    return customIP;
                }
                else
                {
                    _log?.LogWarning($"GetTrustedIPAddress: Invalid or untrusted custom IP from arguments: {_argsHolder.Args[2]}. Falling back.");
                }
            }

            // Fallback to system detection if no valid custom IP provided
            _log?.LogInformation("GetTrustedIPAddress: No valid command-line IP override found. Falling back to system-detected trusted IP addresses.");
            return GetSystemTrustedIPAddress(); // Existing fallback logic
        }

        private static IPAddress GetSystemTrustedIPAddress()
        {
            // First, attempt to get IP address from VPN interfaces
            var vpnInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             IsVpnInterface(ni));

            foreach (var ni in vpnInterfaces)
            {
                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        if (IsTrustedIP(ip.Address))
                        {
                            _log?.LogInformation($"Found trusted VPN IP: {ip.Address}");
                            return ip.Address;
                        }
                    }
                }
            }

            // Then, check other interfaces
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            if (IsTrustedIP(ip.Address))
                            {
                                _log?.LogInformation($"Found trusted IP: {ip.Address}");
                                return ip.Address;
                            }
                        }
                    }
                }
            }

            // Fallback to any available IP address
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            _log?.LogWarning($"Returning non-trusted IP: {ip.Address}");
                            return ip.Address;
                        }
                    }
                }
            }

            // No IP found
            _log?.LogError("No IP address found.");
            return null;
        }

        private static bool IsVpnInterface(NetworkInterface ni)
        {
            // Check for common VPN interface types or descriptions
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Ppp)
            {
                return true;
            }

            if (ni.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase) ||
                ni.Name.Contains("VPN", StringComparison.OrdinalIgnoreCase) ||
                ni.Description.Contains("Cisco AnyConnect", StringComparison.OrdinalIgnoreCase) ||
                ni.Description.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase) ||
                ni.Description.Contains("Pulse Secure", StringComparison.OrdinalIgnoreCase) ||
                ni.Description.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) ||
                ni.Description.Contains("GlobalProtect", StringComparison.OrdinalIgnoreCase)) // Add other VPN clients as needed
            {
                return true;
            }

            return false;
        }

        // Checks if the IP is in a trusted range by using JSONHandler's data
        private static bool IsTrustedIP(IPAddress ipAddress)
        {
            var location = _jsonHandler.GetLocationByIpAddress(ipAddress.ToString());
            bool isTrusted = location != null;
            if (isTrusted)
            {
                _log?.LogInformation($"IP {ipAddress} is trusted.");
            }
            else
            {
                _log?.LogWarning($"IP {ipAddress} is not trusted.");
            }
            return isTrusted;
        }

        public static IPAddress GetFallbackIPAddress()
        {
            var fallbackIP = GetIPAddressByInterfaceType(NetworkInterfaceType.Ethernet) ??
                             GetIPAddressByInterfaceType(NetworkInterfaceType.Wireless80211);
            if (fallbackIP == null || fallbackIP == IPAddress.None)
            {
                _log?.LogWarning("No suitable IP address found on preferred interfaces.");
                return null;
            }

            _log?.LogInformation($"Fallback IP address selected: {fallbackIP}");
            return fallbackIP;
        }

        private static IPAddress GetIPAddressByInterfaceType(NetworkInterfaceType type)
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == type && ni.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            return ip.Address;
                        }
                    }
                }
            }
            return null;
        }
    }
}
