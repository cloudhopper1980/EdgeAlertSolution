using EdgeAlertSignalClient.Handlers;
using EdgeAlertSignalClient.Services;
using EdgeAlertSignalClient.Stores;
using EdgeAlertSignalClient.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace EdgeAlertSignalClient.Utilities
{
    public class Utils
    {
        private readonly ILogger<Utils> _log;
        private readonly CommandLineArgsHolder _argsHolder;

        private const string REGISTRY_BASE_PATH = @"HKEY_LOCAL_MACHINE\SOFTWARE\Edgebits\EdgeAlert";
        private const string PREFERRED_AUDIO_DEVICE_VALUE_NAME = "PreferredAudioDeviceID";
        public const string SYSTEM_DEFAULT_AUDIO_DEVICE_ID = "{SYSTEM_DEFAULT}"; // Public constant for default ID

        public Utils(ILogger<Utils> log,
                     CommandLineArgsHolder argsHolder)
        {
            _log = log;
            _argsHolder = argsHolder;
        }

        public string GetComputerName() // Returns the effective UserName
        {
            // Reverted Logic: Always check command line arguments first
            if (_argsHolder.Args != null && _argsHolder.Args.Length > 1 && !string.IsNullOrWhiteSpace(_argsHolder.Args[1]))
            {
                _log.LogInformation($"GetComputerName: Using custom username from args[1]: {_argsHolder.Args[1]}");
                return _argsHolder.Args[1]; // Return Username override from args[1]
            }

            // Default to the user's environment username if no valid argument provided
            _log.LogDebug($"GetComputerName: No valid arg found. Returning Environment.UserName: {Environment.UserName}");
            return Environment.UserName;
        }

        public string GetHostName()
        {
            return Environment.MachineName;
        }

        public string ConvertUtcToGmtWithDst(DateTime utcDateTime)
        {
            // Determine the time zone information for GMT
            TimeZoneInfo gmtTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");

            // Check if DST is in effect for the specified UTC time
            bool isDstInEffect = gmtTimeZone.IsDaylightSavingTime(utcDateTime);

            // Convert the UTC time to GMT time
            DateTime gmtDateTime = utcDateTime.ToUniversalTime().AddHours(isDstInEffect ? 1 : 0);

            // Return the GMT time in the same format as the input UTC time
            return gmtDateTime.ToString("dd/MM/yyyy HH:mm:ss");
        }

        public static string ConvertDateFormat(string inputString)
        {
            string inputFormat = "dd/MM/yyyy HH:mm:ss";
            string outputFormat = "ddMMyyyyhhmmss";
            DateTime dateTime;

            if (DateTime.TryParseExact(inputString, inputFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
            {
                return dateTime.ToString(outputFormat);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Asynchronously retrieves a registry value of a specified type.
        /// </summary>
        public async Task<T> GetRegistryValueAsync<T>(string key, T fallbackValue, T defaultValue)
        {
            // This method remains useful for other settings, no changes needed here for the audio device.
            // Consider making this internal if only used within this assembly, or keep public if needed elsewhere.
            try
            {
                // Run synchronous registry access on a background thread
                return await Task.Run(() =>
                {
                    object registryValue = Registry.GetValue(REGISTRY_BASE_PATH, key, null);
                    if (registryValue == null)
                    {
                        _log.LogWarning($"Registry value for {key} not found. Defaulting to {defaultValue}.");
                        return defaultValue;
                    }

                    try
                    {
                        // Handle boolean conversion specifically
                        if (typeof(T) == typeof(bool))
                        {
                            if (registryValue is int regInt) // Check if stored as DWORD (int)
                            {
                                return (T)(object)(regInt != 0);
                            }
                            if (bool.TryParse(registryValue.ToString(), out bool result))
                            {
                                return (T)(object)result;
                            }
                            else
                            {
                                _log.LogError($"Invalid boolean format for registry value {key}: {registryValue}. Defaulting to {defaultValue}.");
                                return defaultValue;
                            }
                        }
                        // Handle integer conversion specifically (e.g., AwayTimeoutMinutes)
                        if (typeof(T) == typeof(int))
                        {
                            if (registryValue is int regInt)
                            {
                                return (T)(object)regInt;
                            }
                            if (int.TryParse(registryValue.ToString(), out int result))
                            {
                                return (T)(object)result;
                            }
                            else
                            {
                                _log.LogError($"Invalid integer format for registry value {key}: {registryValue}. Defaulting to {defaultValue}.");
                                return defaultValue;
                            }
                        }

                        // General type conversion for other types
                        return (T)Convert.ChangeType(registryValue, typeof(T));
                    }
                    catch (Exception convEx)
                    {
                        _log.LogError(convEx, $"Error converting registry value for {key}: '{registryValue}' to type {typeof(T).Name}. Defaulting to {defaultValue}.");
                        return defaultValue;
                    }
                });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Error reading registry value for {key}: {ex.Message}");
                return defaultValue;
            }
        }

        /// <summary>
        /// Asynchronously retrieves the preferred audio device ID from the registry.
        /// </summary>
        /// <returns>The stored Device ID string, or the SYSTEM_DEFAULT_AUDIO_DEVICE_ID constant if not found or invalid.</returns>
        public async Task<string> GetPreferredAudioDeviceIDAsync()
        {
            try
            {
                // Run synchronous registry access on a background thread
                return await Task.Run(() =>
                {
                    object registryValue = Registry.GetValue(REGISTRY_BASE_PATH, PREFERRED_AUDIO_DEVICE_VALUE_NAME, null);

                    if (registryValue is string deviceId && !string.IsNullOrWhiteSpace(deviceId))
                    {
                        _log.LogInformation("Retrieved PreferredAudioDeviceID from registry: {DeviceId}", deviceId);
                        return deviceId;
                    }
                    else
                    {
                        if (registryValue == null)
                        {
                            _log.LogWarning("Registry value for {ValueName} not found. Defaulting to System Default.", PREFERRED_AUDIO_DEVICE_VALUE_NAME);
                        }
                        else
                        {
                            _log.LogWarning("Invalid or empty registry value found for {ValueName}: '{Value}'. Defaulting to System Default.", PREFERRED_AUDIO_DEVICE_VALUE_NAME, registryValue);
                        }
                        return SYSTEM_DEFAULT_AUDIO_DEVICE_ID; // Return default identifier
                    }
                });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error reading {ValueName} from registry. Defaulting to System Default.", PREFERRED_AUDIO_DEVICE_VALUE_NAME);
                return SYSTEM_DEFAULT_AUDIO_DEVICE_ID; // Return default on error
            }
        }

        /// <summary>
        /// Asynchronously writes the preferred audio device ID to the registry.
        /// </summary>
        /// <param name="deviceId">The Device ID string to save, or the SYSTEM_DEFAULT_AUDIO_DEVICE_ID constant.</param>
        /// <returns>Task indicating completion.</returns>
        public async Task SetPreferredAudioDeviceIDAsync(string deviceId)
        {
            try
            {
                // Ensure a value is provided; default to the special identifier if null/empty
                string valueToWrite = string.IsNullOrWhiteSpace(deviceId) ? SYSTEM_DEFAULT_AUDIO_DEVICE_ID : deviceId;

                // Run synchronous registry access on a background thread
                await Task.Run(() =>
                {
                    // Use Registry.SetValue which creates the key/path if it doesn't exist.
                    // Requires appropriate permissions for HKLM.
                    Registry.SetValue(REGISTRY_BASE_PATH, PREFERRED_AUDIO_DEVICE_VALUE_NAME, valueToWrite, RegistryValueKind.String);
                });

                _log.LogInformation("Successfully wrote PreferredAudioDeviceID to registry: '{DeviceId}'", valueToWrite);
            }
            catch (UnauthorizedAccessException uaEx)
            {
                _log.LogError(uaEx, "Permission denied writing {ValueName}='{DeviceId}' to registry path {Path}. Ensure the application runs with sufficient privileges.",
                   PREFERRED_AUDIO_DEVICE_VALUE_NAME, deviceId, REGISTRY_BASE_PATH);
                // Rethrow or handle as appropriate (e.g., notify user)
                throw; // Rethrow permission errors as they likely require user intervention
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error writing {ValueName}='{DeviceId}' to registry.", PREFERRED_AUDIO_DEVICE_VALUE_NAME, deviceId);
                // Handle other errors (e.g., log, maybe notify user)
            }
        }
    }
}

