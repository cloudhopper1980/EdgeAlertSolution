using EdgeAlertSignalClient.Models;
using Microsoft.Extensions.Logging;
using NetTools;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.DirectoryServices.ActiveDirectory;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace EdgeAlertSignalClient.Handlers
{
    public class JSONHandler
    {
        private readonly ILogger<JSONHandler> _log;

        public RootObject Data { get; }

        public JSONHandler(ILogger<JSONHandler> log)
        {
            string json = null;
            string url = "https://edgebitslimiteduksouth.blob.core.windows.net/edgebitslimited/edgealert2.json";

#if DEBUG
            // Load the JSON from the local file if the build configuration is Debug
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "edgealert2.json");
            json = File.ReadAllText(path);

#endif

#if RELEASE
            try
            {
        // Load the JSON from the URL if it is accessible and the build configuration is Release
        using (var httpClient = new HttpClient())
        {
            json = httpClient.GetStringAsync(url).Result;
        }
    }
    catch (Exception)
    {
        // Load the JSON from the local file if the URL is not accessible
        string fallbackPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "edgealert2.json");
        json = File.ReadAllText(fallbackPath);
    }
#endif

            Data = JsonSerializer.Deserialize<RootObject>(json);
            _log = log;
        }

        public Location GetLocationByIpAddress(string ipAddress)
        {
            _log.LogInformation($"GetLocationByIpAddress:");
            if (IPAddress.TryParse(ipAddress, out var ip))
            {
                var location = Data.Locations.FirstOrDefault(l =>
                {
                    if (!string.IsNullOrEmpty(l.IpAddressLow) && !string.IsNullOrEmpty(l.IpAddressHigh)
                        && IPAddress.TryParse(l.IpAddressLow, out var ipAddressLow) && IPAddress.TryParse(l.IpAddressHigh, out var ipAddressHigh))
                    {
                        return new IPAddressRange(ipAddressLow, ipAddressHigh).Contains(ip);
                    }
                    return false;
                });
                return location;
            }
            return null;
        }

        public ObservableCollection<Location> GetLocationsWithBlankIPs(string domain)
        {
            var locationsWithBlankIPs = new ObservableCollection<Location>(
                Data.Locations
                    .Where(l => l.Domains != null && l.Domains.Any(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(l => l.Practice)  // Sorting here
            );

            return locationsWithBlankIPs;
        }

        public Location GetLocationByODS(string ods)
        {
            var location = Data.Locations.FirstOrDefault(l => l.ODS.Equals(ods, StringComparison.OrdinalIgnoreCase));
            return location;
        }

        public List<string> GetSimilarODS(string ods)
        {
            // Check for null, empty, or whitespace-only input and return an empty list
            if (string.IsNullOrWhiteSpace(ods))
            {
                return new List<string>();
            }

            // Ensure ods is of a reasonable length (e.g., at least 3 characters) and contains only alphanumeric characters
            ods = ods.Trim(); // Remove any surrounding whitespace
            if (ods.Length < 3 || !ods.All(char.IsLetterOrDigit))
            {
                return new List<string>();
            }

            // Take the first 6 characters from the input ODS for matching
            string prefixToMatch = ods.Length >= 6 ? ods.Substring(0, 6) : ods;

            return Data.Locations
                .Where(l =>
                    // Matches if either the ODS in Data.Locations starts with the prefix
                    l.ODS.StartsWith(prefixToMatch, StringComparison.OrdinalIgnoreCase)
                    // Or if the prefix itself starts with any ODS in Data.Locations
                    || prefixToMatch.StartsWith(l.ODS.Substring(0, Math.Min(6, l.ODS.Length)), StringComparison.OrdinalIgnoreCase))
                .Select(l => l.ODS)
                .Distinct() // To eliminate any duplicates
                .ToList();
        }

        public Location GetLocationByPractice(string practice)
        {
            var location = Data.Locations.FirstOrDefault(l => l.Practice.Equals(practice, StringComparison.OrdinalIgnoreCase));
            return location;
        }

        public bool IsTrustedDomain(string domain)
        {
            _log.LogInformation($"Checking if domain '{domain}' is trusted.");

            if (Data.Locations == null || string.IsNullOrWhiteSpace(domain))
            {
                _log.LogWarning($"Either 'Data.Locations' is null or the provided domain '{domain}' is empty.");
                return false;
            }

            // Check if any of the locations contain the specified domain
            bool isTrusted = Data.Locations.Any(location =>
                location.Domains != null &&
                location.Domains.Any(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase))
            );

            if (isTrusted)
            {
                _log.LogInformation($"Domain '{domain}' is a trusted domain.");
            }
            else
            {
                _log.LogInformation($"Domain '{domain}' is not a trusted domain.");
            }

            return isTrusted;
        }
    }
}
