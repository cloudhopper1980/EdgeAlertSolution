using CommunityToolkit.Mvvm.Messaging;
using EdgeAlertSignalClient.Extensions;
using EdgeAlertSignalClient.Message;
using EdgeAlertSignalClient.Services;
using EdgeAlertSignalClient.Stores;
using EdgeAlertSignalClient.Utilities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace EdgeAlertSignalClient.Handlers
{
    public class NetworkHandler
    {
        private readonly ILogger<NetworkHandler> _log;
        private readonly JSONHandler _jsonHandler;
        private readonly CommandLineArgsHolder _argsHolder;

        private readonly FeatureSettingsCache _featureSettingsCache;
        private readonly Utils _utils;
        private readonly RoomStore _roomStore;

        // <<< Define Feature Name Constants >>>
        private const string InternetCheckFeatureName = "InternetCheck-Feature";

        public NetworkHandler(
                    ILogger<NetworkHandler> log,
                    JSONHandler jsonHandler,
                    CommandLineArgsHolder argsHolder,
                    FeatureSettingsCache featureSettingsCache,
                    Utils utils,
                    RoomStore roomStore)
        {
            _log = log;
            _jsonHandler = jsonHandler;
            _argsHolder = argsHolder;
            _featureSettingsCache = featureSettingsCache;
            _utils = utils;
            _roomStore = roomStore;
        }

        public string GetTrustedDomainOrSuffix()
        {
            // Check command line args first, unconditionally
            if (_argsHolder.Args != null && _argsHolder.Args.Length > 3) // Index 3 for domain
            {
                string customDomain = _argsHolder.Args[3];
                if (string.IsNullOrEmpty(customDomain) || customDomain.Equals("null", StringComparison.OrdinalIgnoreCase))
                {
                    _log.LogInformation($"GetTrustedDomainOrSuffix: Custom domain override is 'null'. No trusted domain will be used.");
                    return null; // Explicitly no domain override
                }
                _log.LogInformation($"GetTrustedDomainOrSuffix: Using custom domain from arguments: {customDomain}");
                return customDomain; // Use the custom domain provided via arguments
            }

            // Fallback to system detection if no command-line override provided
            _log.LogInformation($"GetTrustedDomainOrSuffix: No domain override in args. Falling back to system-detected domain/suffixes.");
            var combinedDnsSuffixes = GetCombinedDnsSuffixes();
            _log.LogInformation($"GetTrustedDomainOrSuffix: Combined system domain names/DNS suffixes: {string.Join(", ", combinedDnsSuffixes)}");

            foreach (var dnsSuffix in combinedDnsSuffixes)
            {
                _log.LogInformation($"GetTrustedDomainOrSuffix: Checking system domain name: {dnsSuffix}");
                if (_jsonHandler.IsTrustedDomain(dnsSuffix))
                {
                    _log.LogInformation($"GetTrustedDomainOrSuffix: Found trusted system domain: {dnsSuffix}");
                    return dnsSuffix;
                }
            }

            _log.LogWarning("GetTrustedDomainOrSuffix: No trusted domain found (neither custom nor system).");
            return null; // Return null if no trusted domain found
        }

        // No changes in these methods, but they are called in GetTrustedDomainOrSuffix
        public IEnumerable<string> GetCombinedDnsSuffixes()
        {
            var domainName = GetDomainName();
            var dnsSuffixes = GetAllDnsSuffixes() ?? Enumerable.Empty<string>();
            var dnsSuffixesV2 = GetAllDnsSuffixesV2() ?? Enumerable.Empty<string>();

            var combinedDnsSuffixes = new List<string>();

            if (!string.IsNullOrWhiteSpace(domainName))
            {
                combinedDnsSuffixes.Add(domainName);
            }

            combinedDnsSuffixes.AddRange(dnsSuffixes);
            combinedDnsSuffixes.AddRange(dnsSuffixesV2);

            return combinedDnsSuffixes.Distinct().Where(suffix => !string.IsNullOrWhiteSpace(suffix));
        }

        public string GetDomainName()
        {
            try
            {
                using (var domain = Domain.GetDomain(new DirectoryContext(DirectoryContextType.Domain)))
                {
                    return domain.Name;
                }
            }
            catch (Exception ex)
            {
                _log.LogError($"Exception in GetDomainName: Returning 'No Active Directory': {ex.Message}");
                return "No Active Directory";
            }
        }

        public IEnumerable<string> GetAllDnsSuffixes()
        {
            var dnsSuffixes = new List<string>();
            try
            {
                foreach (NetworkInterface netInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    IPInterfaceProperties properties = netInterface.GetIPProperties();
                    if (!string.IsNullOrEmpty(properties.DnsSuffix))
                    {
                        dnsSuffixes.Add(properties.DnsSuffix);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError($"GetAllDnsSuffixes: Exception: {ex.Message}");
            }

            return dnsSuffixes; // Return the list of DNS suffixes
        }

        public IEnumerable<string> GetAllDnsSuffixesV2()
        {
            var dnsSuffixes = new List<string>();
            try
            {
                // Query WMI for network adapter configuration
                ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_NetworkAdapterConfiguration");
                foreach (ManagementObject queryObj in searcher.Get())
                {
                    // Check if the IPEnabled property is true, indicating an active network interface
                    if ((bool)queryObj["IPEnabled"])
                    {
                        string dnsSuffix = queryObj["DNSDomain"] as string;
                        if (!string.IsNullOrEmpty(dnsSuffix))
                        {
                            dnsSuffixes.Add(dnsSuffix);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log exception (make sure to use your logging approach here)
                _log.LogError($"GetAllDnsSuffixesV2: Exception: {ex.Message}");
            }

            return dnsSuffixes;
        }

        public async Task<bool> IsInternetAvailableAsync(CancellationToken cancellationToken)
        {
            bool isInternetAvailable = false; // Start assuming no internet access

            // Removed the entire Feature Check block as requested.
            // The method will now always proceed with the internet checks.

            try
            {
                _log?.LogInformation("IsInternetAvailableAsync: Performing internet checks..."); // Updated log message as check is now always performed

                // --- DNS Check ---
                string[] domains = { "bbc.co.uk", "google.com" }; // Example domains
                foreach (string domain in domains)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _log?.LogWarning("IsInternetAvailableAsync: Cancellation requested during DNS check.");
                        break; // Exit loop if cancelled
                    }
                    try
                    {
                        // Note: Ensure your .NET version/extensions support cancellation here.
                        // Standard Dns.GetHostAddressesAsync might not directly accept CancellationToken in all versions.
                        // Your original code used .WithCancellation(), implying an extension method was in use.
                        // If using standard .NET Core/5+, consider Task.Run or check for cancellable overloads.
                        // For simplicity, retaining the structure, assuming .WithCancellation() or similar works.
                        // Example using a hypothetical cancellable overload:
                        // var addresses = await Dns.GetHostAddressesAsync(domain, cancellationToken).ConfigureAwait(false);

                        // Using the original pattern:
                        var addresses = await Dns.GetHostAddressesAsync(domain)
                                                 /*.WithCancellation(cancellationToken)*/ // Keep if this extension method exists and works
                                                 .ConfigureAwait(false); // Added ConfigureAwait(false) for good practice in library code

                        if (addresses != null && addresses.Length > 0)
                        {
                            isInternetAvailable = true;
                            _log?.LogInformation($"IsInternetAvailableAsync: DNS resolution successful for {domain}. Internet assumed available.");
                            break; // Stop checking once one domain resolves
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _log?.LogWarning($"IsInternetAvailableAsync: DNS check for {domain} cancelled.");
                        // Decide if cancellation should stop all checks or just this domain
                        break; // Stop further checks if cancelled
                    }
                    catch (System.Net.Sockets.SocketException dnsEx) // Catch specific DNS errors
                    {
                        _log?.LogWarning($"IsInternetAvailableAsync: DNS resolution failed for {domain}: {dnsEx.Message}");
                        // Continue to the next domain or the HTTP check
                    }
                    catch (Exception ex) // Catch any other unexpected error during DNS check
                    {
                        _log?.LogWarning($"IsInternetAvailableAsync: Unexpected error during DNS check for {domain}: {ex.Message}");
                        // Continue to the next domain or the HTTP check
                    }
                }

                // --- HTTP Check (Fallback) ---
                // Attempt HTTP check if DNS checks failed or were cancelled before confirming connectivity
                if (!isInternetAvailable && !cancellationToken.IsCancellationRequested)
                {
                    _log?.LogInformation("IsInternetAvailableAsync: DNS checks failed or inconclusive. Attempting HTTP check...");

                    // It's generally better to use IHttpClientFactory than creating HttpClient directly
                    // but following the original pattern for this example.
                    using (var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                    {
                        try
                        {
                            // Use a reliable, lightweight endpoint. HTTPS is preferred.
                            // Google's generate_204 is common, or use a well-known captive portal detection URL.
                            var httpResponse = await httpClient.GetAsync("https://connectivitycheck.gstatic.com/generate_204", cancellationToken)
                                                             .ConfigureAwait(false); // Added ConfigureAwait

                            isInternetAvailable = httpResponse.IsSuccessStatusCode; // Check for 2xx status code
                            _log?.LogInformation($"IsInternetAvailableAsync: HTTP check result: {isInternetAvailable} (StatusCode: {httpResponse.StatusCode})");
                        }
                        catch (OperationCanceledException)
                        {
                            _log?.LogWarning($"IsInternetAvailableAsync: HTTP check cancelled.");
                            // isInternetAvailable remains false
                        }
                        catch (HttpRequestException httpEx) // Catch specific HTTP errors
                        {
                            _log?.LogWarning(httpEx, "IsInternetAvailableAsync: Exception during HTTP connectivity check.");
                            isInternetAvailable = false; // Assume unavailable on HTTP exception
                        }
                        catch (Exception ex) // Catch any other unexpected error during HTTP check
                        {
                            _log?.LogError(ex, "IsInternetAvailableAsync: Unexpected error during HTTP check.");
                            isInternetAvailable = false; // Assume unavailable on other exceptions
                        }
                    }
                }
            }
            // Removed the 'else' block related to the feature being disabled.
            catch (Exception ex) // Catch unexpected errors in the overall process
            {
                _log?.LogError(ex, "IsInternetAvailableAsync: Unhandled error checking internet availability.");
                isInternetAvailable = false; // Default to false on any outer error
            }

            // Send status message - ensure WeakReferenceMessenger and ConnectivityStatusMessage are defined and accessible
            WeakReferenceMessenger.Default.Send(new ConnectivityStatusMessage(isInternetAvailable));

            _log?.LogInformation($"IsInternetAvailableAsync: Final internet availability status: {isInternetAvailable}");
            return isInternetAvailable;
        }
    }
}
