using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using EdgeAlertSignalClient.Models;
using EdgeAlertSignalClient.Handlers;
using Polly;
using Polly.Retry;

namespace EdgeAlertSignalClient.Services
{
    /// <summary>
    /// Service for sending user time logs to the server
    /// </summary>
    public class UserTimeLogService
    {
        private readonly ILogger<UserTimeLogService> _logger;
        private readonly AzureHandler _azureHandler;
        private readonly HttpClient _httpClient;
        private readonly AsyncRetryPolicy<HttpResponseMessage> _httpRetryPolicy;

        // The Azure Function endpoint for logging user time
        private const string LogUserTimeEndpoint = "/LogUserTime";

        /// <summary>
        /// Initializes a new instance of the UserTimeLogService
        /// </summary>
        public UserTimeLogService(
            ILogger<UserTimeLogService> logger,
            AzureHandler azureHandler)
        {
            _logger = logger;
            _azureHandler = azureHandler;
            _httpClient = new HttpClient();

            // Create a retry policy for HTTP requests
            _httpRetryPolicy = Policy
                .Handle<HttpRequestException>()
                .OrResult<HttpResponseMessage>(response => !response.IsSuccessStatusCode)
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }

        /// <summary>
        /// Sends user state logs to the Azure Function
        /// </summary>
        public async Task<bool> SendLogsAsync(List<UserStateLog> logs)
        {
            if (logs == null || logs.Count == 0)
            {
                _logger.LogInformation("No logs to send");
                return true;
            }

            try
            {
                // Build the Azure Function URL with the function key
                string functionKey = _azureHandler._settings.DefaultHostKey;

                string url = $"{GetBaseFunctionUrl()}{LogUserTimeEndpoint}";

                if (!string.IsNullOrEmpty(functionKey)) {
                     url += $"?code={functionKey}";
                }

                // ****** START CHANGE ******
                // Configure serializer options to write enums as strings
                var options = new JsonSerializerOptions
                {
                    Converters = { new JsonStringEnumConverter() }
                };

                // Serialize the logs to JSON using the specified options
                string jsonContent = JsonSerializer.Serialize(logs, options);
                // ****** END CHANGE ******

                _logger.LogInformation($"Sending JSON: {jsonContent}"); // Added log to see JSON

                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Send the request with retry policy
                var response = await _httpRetryPolicy.ExecuteAsync(() => _httpClient.PostAsync(url, content));

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"Successfully sent {logs.Count} logs to server");
                    return true;
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Failed to send logs. Status code: {response.StatusCode}, Content: {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending logs: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the base URL for Azure Functions
        /// </summary>
        private string GetBaseFunctionUrl()
        {
#if DEBUG
            //return "http://localhost:7071/api"; // Local development URL
            return "https://edgealertfunc.azurewebsites.net/api";
#else
            return "https://edgealertfunc.azurewebsites.net/api"; // Production URL
#endif
        }
    }
}