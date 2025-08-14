using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi; // Requires NAudio nuget package
using NAudio.Wave;         // Required for WaveOutCapabilities

namespace EdgeAlertSignalClient.Services
{
    /// <summary>
    /// Represents details of an audio output device.
    /// </summary>
    public class AudioDevice
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int WaveOutDeviceNumber { get; set; } = -1;
        public bool IsAvailable { get; set; } = true; 
    }

    /// <summary>
    /// Service responsible for enumerating audio output devices using NAudio.
    /// </summary>
    public class AudioDeviceService
    {
        private readonly ILogger<AudioDeviceService> _logger;
        // Special ID representing the system default device
        public const string SystemDefaultDeviceId = "{SYSTEM_DEFAULT}";

        public AudioDeviceService(ILogger<AudioDeviceService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Enumerates active audio output devices available on the system.
        /// Includes a representation for the System Default device.
        /// </summary>
        /// <returns>A list of AudioDevice objects.</returns>
        public List<AudioDevice> GetActiveOutputDevices()
        {
            _logger.LogDebug("Enumerating active audio output devices...");
            var devices = new List<AudioDevice>();
            MMDeviceEnumerator enumerator = null;

            try
            {
                // Add the "System Default" option first
                devices.Add(new AudioDevice
                {
                    Id = SystemDefaultDeviceId,
                    Name = "System Default Device",
                    WaveOutDeviceNumber = -1 // NAudio uses -1 for default
                });

                enumerator = new MMDeviceEnumerator();
                var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                _logger.LogInformation("Found {Count} active MMDevice endpoints.", endpoints.Count);

                // Try mapping MMDevices to WaveOut device numbers
                for (int n = -1; n < WaveOut.DeviceCount; n++) // Start from -1 for the default mapper device
                {
                    WaveOutCapabilities capabilities;
                    try
                    {
                        capabilities = WaveOut.GetCapabilities(n);
                        _logger.LogDebug("WaveOut Device {Number}: {ProductName}", n, capabilities.ProductName);
                    }
                    catch (Exception waveEx)
                    {
                        _logger.LogWarning(waveEx, "Could not get WaveOut capabilities for device number {Number}.", n);
                        continue; // Skip this device number if capabilities fail
                    }

                    // Find the corresponding MMDevice
                    // Note: This matching based on ProductName is not guaranteed to be perfect,
                    // especially with virtual audio devices or complex setups.
                    // MMDevice ID is generally more reliable for persistence.
                    MMDevice matchingEndpoint = endpoints.FirstOrDefault(e =>
                        e.DeviceFriendlyName.Contains(capabilities.ProductName, StringComparison.OrdinalIgnoreCase) ||
                        e.FriendlyName.Contains(capabilities.ProductName, StringComparison.OrdinalIgnoreCase));

                    if (matchingEndpoint != null)
                    {
                        // Check if we already added this MMDevice ID (to avoid duplicates if name matching is ambiguous)
                        if (!devices.Any(d => d.Id == matchingEndpoint.ID))
                        {
                            devices.Add(new AudioDevice
                            {
                                Id = matchingEndpoint.ID, // Use the reliable MMDevice ID
                                Name = $"{capabilities.ProductName} ({matchingEndpoint.DeviceFriendlyName})", // Combine names for clarity
                                WaveOutDeviceNumber = n
                            });
                            _logger.LogInformation("Mapped WaveOut Device {WaveOutNum} ('{WaveOutName}') to MMDevice '{MMDeviceId}' ('{MMDeviceName}')",
                                n, capabilities.ProductName, matchingEndpoint.ID, matchingEndpoint.DeviceFriendlyName);
                        }
                        else
                        {
                            _logger.LogWarning("Skipping duplicate MMDevice ID '{MMDeviceId}' found for WaveOut Device {WaveOutNum} ('{WaveOutName}')",
                               matchingEndpoint.ID, n, capabilities.ProductName);
                        }
                    }
                    else if (n == -1) // Handle the WaveOut default device explicitly if no specific endpoint matched
                    {
                        _logger.LogDebug("WaveOut Device -1 (Default Mapper) did not directly match an active MMDevice endpoint by name.");
                        // The "System Default Device" entry already added handles this case conceptually.
                    }
                    else
                    {
                        _logger.LogWarning("Could not find matching MMDevice endpoint for WaveOut device {Number} ('{ProductName}')", n, capabilities.ProductName);
                        // Optionally add WaveOut device even without MMDevice match? - Decided against for now to prefer MMDevice IDs.
                        /*
                         devices.Add(new AudioDevice
                         {
                             Id = $"WAVEOUT_{n}", // Create a fallback ID
                             Name = capabilities.ProductName + " (WaveOut Only)",
                             WaveOutDeviceNumber = n
                         });
                        */
                    }
                }

                // Clean up MMDevice resources if endpoints were enumerated
                foreach (var ep in endpoints) { ep.Dispose(); }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enumerating audio devices.");
                // Return the list containing at least "System Default" if possible, or an empty list on critical failure
                if (!devices.Any(d => d.Id == SystemDefaultDeviceId))
                {
                    // Ensure default is added even on error if list is empty
                    devices.Add(new AudioDevice { Id = SystemDefaultDeviceId, Name = "System Default Device", WaveOutDeviceNumber = -1 });
                }
            }
            finally
            {
                enumerator?.Dispose(); // Dispose MMDeviceEnumerator
            }

            _logger.LogInformation("Finished enumerating devices. Found {Count} including System Default.", devices.Count);
            return devices;
        }

        /// <summary>
        /// Finds the WaveOut device number associated with a given MMDevice ID.
        /// </summary>
        /// <param name="deviceId">The MMDevice ID to search for.</param>
        /// <returns>The corresponding WaveOut device number, or -1 if not found or for default.</returns>
        public int GetWaveOutDeviceNumberFromId(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId) || deviceId == SystemDefaultDeviceId)
            {
                return -1; // -1 represents the default device for WaveOutEvent
            }

            // Re-enumerate to get the current mapping (device numbers can change)
            // In a production app, consider caching this carefully or subscribing to device change notifications
            // for better performance, but re-enumerating is safer for correctness if devices change.
            var devices = GetActiveOutputDevices();
            var foundDevice = devices.FirstOrDefault(d => d.Id == deviceId);

            if (foundDevice != null)
            {
                _logger.LogDebug("Found WaveOutDeviceNumber {Number} for Device ID {Id}", foundDevice.WaveOutDeviceNumber, deviceId);
                return foundDevice.WaveOutDeviceNumber;
            }
            else
            {
                _logger.LogWarning("Could not find matching WaveOutDeviceNumber for Device ID {Id}. Returning default (-1).", deviceId);
                return -1; // Fallback to default if ID not found
            }
        }
    }
}