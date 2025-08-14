using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace EdgeAlertSignalClient.Handlers
{
    public class IPAddressMonitor
    {
        private IPAddress _currentIPAddress;
        private readonly ILogger<IPAddressMonitor> _log;
        private readonly CommandLineArgsHolder _argsHolder;
        private readonly NetworkHandler _networkHandler;

        // Event to notify about IP address changes (now using IPAddress instead of string)
        public event EventHandler<IPAddress> OnIPAddressChanged;

        public IPAddressMonitor(ILogger<IPAddressMonitor> log, CommandLineArgsHolder argsHolder, NetworkHandler networkHandler)        
        {
            _log = log;
            _argsHolder = argsHolder;
            _networkHandler = networkHandler;
            _log.LogInformation("IPAddressMonitor: Initialising");

            // Initialize with current IP as IPAddress (null-safe handling)
            _currentIPAddress = IPAddressHandler.GetTrustedIPAddress() ?? IPAddress.None;
            _log.LogInformation($"IPAddressMonitor: _currentIPAddress: {_currentIPAddress}");

            // Subscribe to network address changes
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

            _log.LogInformation("IPAddressMonitor: Initialised");
        }

        // Network address change handler
        private void OnNetworkAddressChanged(object sender, EventArgs e)
        {
            _log.LogInformation("OnNetworkAddressChanged triggered.");

            try
            {
                IPAddress newIPAddress = GetCurrentLocalIPAddress();

                // Check if IP has changed, and invoke the event accordingly
                if (!Equals(newIPAddress, _currentIPAddress))
                {
                    _log.LogInformation("IP address has changed.");
                    _currentIPAddress = newIPAddress;
                }
                else
                {
                    _log.LogInformation("IP address remains the same.");
                }

                // Raise the IP address change event
                OnIPAddressChanged?.Invoke(this, _currentIPAddress);
            }
            catch (Exception ex)
            {
                _log.LogError($"Error in OnNetworkAddressChanged: {ex.Message}");
            }
        }

        private IPAddress GetCurrentLocalIPAddress()
        {
            _log.LogInformation("GetCurrentLocalIPAddress:");
            return IPAddressHandler.GetTrustedIPAddress() ?? IPAddress.None;
        }

        private IPAddress GetIPAddressByInterfaceType(NetworkInterfaceType type)
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

        private IPAddress GetFallbackIPAddress()
        {
            // Attempt to get an IP address based on preferred interface types
            return GetIPAddressByInterfaceType(NetworkInterfaceType.Ethernet) ??
                   GetIPAddressByInterfaceType(NetworkInterfaceType.Wireless80211) ??
                   IPAddress.None;
        }
    }
}
