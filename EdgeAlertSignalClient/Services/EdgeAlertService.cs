using System.Net.Http;
using Microsoft.AspNetCore.SignalR.Client;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using EdgeAlertSignalClient.Handlers;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System;
using System.Threading;
using EdgeAlertSignalClient.Models;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace EdgeAlertSignalClient.Services
{
    public interface IEdgeAlertService
    {
        Task<NegotiateData> NegotiateAsync(EdgeUser user);
        Task SendMessageAsync(EdgeUser user);
        Task<UpdateUserResponse> UpdateUser(EdgeUser user);
        Task UserDisconnected(EdgeUser user);
        Task RespondToAlertAsync(Responder responder);
        HubConnection ConnectToHub(NegotiateData negotiateData);
        Task<string> FetchNewToken();
        Task<string> GetAwayTimeoutSettingAsync();
        Task<bool> UpdateAwayTimeoutSettingAsync(string minutes);
        Task<bool> ManageReportPermissionsAsync(List<ReportPermission> permissions);
        Task<List<ReportPermission>> GetUserReportPermissionsAsync(string userId);
        Task<List<SummarizedReportData>> GetSummarizedReportDataAsync(DateTime startDate, DateTime endDate, string filterUser, string filterOds);
        Task<List<DetailedReportData>> GetDetailedReportDataAsync(DateTime startDate, DateTime endDate, string filterUser, string filterMachine, string filterOds);
        Task<List<string>> GetPermittedOdsAsync();
        Task<List<string>> GetPermittedUsersForOdsAsync(string targetOds);
        Task<bool> CheckHasAnyReportAccessAsync();

        /// <summary>
        /// Gets the effective enablement status for a specific feature, user, and ODS code from the backend.
        /// The backend function implements the User > ODS > Global hierarchy logic.
        /// </summary>
        /// <param name="featureName">The name of the feature to check (e.g., "UserTimeTracking-Feature").</param>
        /// <param name="userName">The username to check against.</param>
        /// <param name="odsCode">The ODS code to check against.</param>
        /// <returns>A Task returning true if the feature is effectively enabled, false otherwise.</returns>
        Task<bool> GetEffectiveFeatureSettingAsync(string featureName, string userName, string odsCode);
        Task<List<FeatureSettingInfo>> GetAllFeatureSettingsAsync();

        /// <summary>
        /// Sets or updates a specific feature setting via the backend API.
        /// </summary>
        /// <param name="featureName">The name of the feature.</param>
        /// <param name="scopeIdentifier">The scope identifier (e.g., GLOBAL:, ODS_CODE, USER_NAME).</param>
        /// <param name="isEnabled">The desired state (true or false).</param>
        /// <returns>True if the operation was successful, false otherwise.</returns>
        Task<bool> SetFeatureSettingAsync(string featureName, string scopeIdentifier, bool isEnabled);

        /// <summary>
        /// Deletes a specific feature setting via the backend API, allowing inheritance to take over.
        /// </summary>
        /// <param name="featureName">The name of the feature.</param>
        /// <param name="scopeIdentifier">The scope identifier to delete.</param>
        /// <returns>True if the operation was successful or the setting didn't exist, false on error.</returns>
        Task<bool> DeleteFeatureSettingAsync(string featureName, string scopeIdentifier);

        /// <summary>
        /// Retrieves a list of distinct, available feature names from the backend.
        /// </summary>
        /// <returns>A list of feature name strings, or null/empty list on error.</returns>
        Task<List<string>> GetAvailableFeaturesAsync();

        /// <summary>
        /// Calls the backend to set the enabled state for a feature across ALL ODS scopes.
        /// </summary>
        /// <param name="featureName">The name of the feature.</param>
        /// <param name="isEnabled">The desired state (true or false).</param>
        /// <returns>True if the backend operation was successful, false otherwise.</returns>
        Task<bool> SetBulkOdsSettingsAsync(string featureName, bool isEnabled);

        /// <summary>
        /// Calls the backend to set the enabled state for a feature across ALL USER scopes.
        /// </summary>
        /// <param name="featureName">The name of the feature.</param>
        /// <param name="isEnabled">The desired state (true or false).</param>
        /// <returns>True if the backend operation was successful, false otherwise.</returns>
        Task<bool> SetBulkUserSettingsAsync(string featureName, bool isEnabled);

        /// <summary>
        /// Calls the backend to delete all ODS-specific overrides for a feature.
        /// </summary>
        /// <param name="featureName">The name of the feature.</param>
        /// <returns>True if the backend operation was successful, false otherwise.</returns>
        Task<bool> DeleteAllOdsOverridesAsync(string featureName);

        /// <summary>
        /// Calls the backend to delete all User-specific overrides for a feature.
        /// </summary>
        /// <param name="featureName">The name of the feature.</param>
        /// <returns>True if the backend operation was successful, false otherwise.</returns>
        Task<bool> DeleteAllUserOverridesAsync(string featureName);

    }

    public class EdgeAlertService : IEdgeAlertService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<EdgeAlertService> _log;
        private readonly AzureHandler _azureHandler;
        private readonly AsyncRetryPolicy<HttpResponseMessage> _httpRetryPolicy;
        private string _userName;
        private string _hostName;
        private string _userId;

#if DEBUG
        //const string baseFunctionUrl = "http://localhost:7071/api"; // Adjust for local dev
        const string baseFunctionUrl = "https://edgealertfunc.azurewebsites.net/api";
#else
        const string baseFunctionUrl = "https://edgealertfunc.azurewebsites.net/api";
#endif

        public EdgeAlertService(ILogger<EdgeAlertService> log, AzureHandler azureHandler)
        {
            _httpClient = new HttpClient();
            _log = log;
            _azureHandler = azureHandler;

            _httpRetryPolicy = Policy
                .Handle<HttpRequestException>()
                .OrResult<HttpResponseMessage>(response => !response.IsSuccessStatusCode)
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }

        public async Task<NegotiateData> NegotiateAsync(EdgeUser user)
        {
            // Setting user specific variables
            _userName = user.UserName;
            _hostName = user.HostName;
            _userId = $"{_userName.ToUpper()}-{_hostName.ToUpper()}";

            var negotiateUrl = $"{baseFunctionUrl}/negotiateV2";

            HttpResponseMessage response = null;

            try
            {
                // Using block ensures HttpRequestMessage is disposed after use
                using (var request = new HttpRequestMessage())
                {
                    request.Method = HttpMethod.Post;
                    request.RequestUri = new Uri(negotiateUrl);
                    request.Headers.Add("x-ms-signalr-userid", _userId);

                    // Execute the HTTP request using the retry policy
                    response = await _httpRetryPolicy.ExecuteAsync(() => _httpClient.SendAsync(request));

                    // Reading and deserializing response content
                    var negotiateContent = await response.Content.ReadAsStringAsync();
                    var negotiateData = JsonSerializer.Deserialize<NegotiateData>(negotiateContent);

                    return negotiateData;
                }
            }
            finally
            {
                // Dispose the HttpResponseMessage to free up system resources
                response?.Dispose();
            }
        }

        public HubConnection ConnectToHub(NegotiateData negotiateData)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(negotiateData.Url, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(negotiateData.AccessToken);
                })
                .WithAutomaticReconnect()
                .Build();

            return connection;
        }

        public async Task SendMessageAsync(EdgeUser user)
        {
            await _azureHandler.InitializationTask; // Wait for Azure keys to be loaded
            var functionKey = _azureHandler._settings.SendMessageFunctionKey;
            var sendMessageUrl = $"{baseFunctionUrl}/sendmessage?code={functionKey}";
            var content = new StringContent(JsonSerializer.Serialize(user), Encoding.UTF8, "application/json");

            var response = await _httpRetryPolicy.ExecuteAsync(() => _httpClient.PostAsync(sendMessageUrl, content));
            if (!response.IsSuccessStatusCode)
            {
                _log.LogError($"SendMessageAsync failed with status code: {response.StatusCode}");
            }
        }

        public async Task<UpdateUserResponse> UpdateUser(EdgeUser user)
        {
            await _azureHandler.InitializationTask; // Wait for Azure keys to be loaded
            var functionKey = _azureHandler._settings.UpdateUserFunctionKey;
            var updateUrl = $"{baseFunctionUrl}/UpdateUser?code={functionKey}";
            string userId = $"{user.UserName.ToUpper()}-{user.HostName.ToUpper()}";
            string clientVersion = "2.1";

            try
            {
                var response = await _httpRetryPolicy.ExecuteAsync(async () =>
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Post, updateUrl))
                    {
                        request.Headers.Add("x-ms-signalr-userid", userId);
                        request.Headers.Add("Client-Version", clientVersion);
                        request.Content = new StringContent(JsonSerializer.Serialize(user), Encoding.UTF8, "application/json");

                        return await _httpClient.SendAsync(request);
                    }
                });

                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<UpdateUserResponse>(responseContent);
                    if (result == null)
                    {
                        _log.LogError("Deserialized result is null.");
                        return new UpdateUserResponse { UserName = user.UserName, IsBlocked = false };
                    }

                    if (result.IsBlocked)
                    {
                        _log.LogWarning($"User {userId} is blocked");
                    }
                    else
                    {
                        _log.LogInformation("User updated successfully");
                    }

                    return result;
                }
                else
                {
                    _log.LogError($"UpdateUser failed with status code: {response.StatusCode}, Content: {responseContent}");
                    return new UpdateUserResponse { UserName = user.UserName, IsBlocked = false };
                }
            }
            catch (Exception ex)
            {
                _log.LogError($"UpdateUser: Exception occurred while updating user {user.HostName}. Error: {ex.Message}");
                return new UpdateUserResponse { UserName = user.UserName, IsBlocked = false };
            }
        }

        public async Task UserDisconnected(EdgeUser user)
        {
            await _azureHandler.InitializationTask; // Wait for Azure keys to be loaded
            var functionKey = _azureHandler._settings.DisconnectUserFunctionKey;
            var disconnectUrl = $"{baseFunctionUrl}/UserDisconnected?code={functionKey}";

            string userId = $"{user.UserName.ToUpper()}-{user.HostName.ToUpper()}";
            var request = new HttpRequestMessage(HttpMethod.Post, disconnectUrl)
            {
                Headers = { { "x-ms-signalr-userid", userId } },
                Content = new StringContent(JsonSerializer.Serialize(user), Encoding.UTF8, "application/json")
            };

            HttpResponseMessage response = null;
            try
            {
                response = await _httpRetryPolicy.ExecuteAsync(() => _httpClient.SendAsync(request));

                if (!response.IsSuccessStatusCode)
                {
                    _log.LogError($"UserDisconnected failed with status code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _log.LogError($"UserDisconnected: Exception occurred while disconnecting user {user.HostName}. Error: {ex.Message}");
            }
            finally
            {
                request.Dispose();
                response?.Dispose();
            }
        }

        public async Task RespondToAlertAsync(Responder responder)
        {
            await _azureHandler.InitializationTask; // Wait for Azure keys to be loaded
            var functionKey = _azureHandler._settings.RespondToAlertFunctionKey;
            var respondToAlertUrl = $"{baseFunctionUrl}/RespondToAlert?code={functionKey}";
            const string customHeaderValue = "UseGroupUpdate";

            var request = new HttpRequestMessage(HttpMethod.Post, respondToAlertUrl)
            {
                Headers = { { "Use-Group-Update", customHeaderValue } },
                Content = new StringContent(JsonSerializer.Serialize(responder), Encoding.UTF8, "application/json")
            };

            HttpResponseMessage response = null;
            try
            {
                response = await _httpRetryPolicy.ExecuteAsync(() => _httpClient.SendAsync(request));

                if (!response.IsSuccessStatusCode)
                {
                    _log.LogError($"RespondToAlertAsync failed with status code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _log.LogError($"RespondToAlertAsync: Exception occurred while responding to alert. Error: {ex.Message}");
            }
            finally
            {
                request.Dispose();
                response?.Dispose();
            }
        }

        public async Task<string> FetchNewToken()
        {
            try
            {
                var negotiateEndpoint = "/negotiateV2";
                var request = new HttpRequestMessage(HttpMethod.Post, $"{baseFunctionUrl}{negotiateEndpoint}")
                {
                    Headers = { { "x-ms-signalr-userid", _userId } }
                };

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var connectionInfo = JsonSerializer.Deserialize<NegotiateData>(content);
                    return connectionInfo.AccessToken;
                }
                else
                {
                    _log.LogError($"FetchNewToken: Unable to fetch new token: {response.StatusCode}");
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _log.LogError($"FetchNewToken: Error fetching new token: {ex}");
                return string.Empty;
            }
        }

        private const string AWAY_TIMEOUT_SETTING_NAME = "AwayTimeoutMinutes"; // Consistent setting name

        /// <summary>
        /// Retrieves the Away Timeout setting from the Azure Function.
        /// </summary>
        /// <returns>The timeout value as a string, or a default/null if not found/error.</returns>
        public async Task<string> GetAwayTimeoutSettingAsync()
        {
            await _azureHandler.InitializationTask; // Wait for Azure keys to be loaded
            // Assuming GetSetting requires a function key, adjust if anonymous
            var functionKey = _azureHandler._settings.DefaultHostKey; // Use the property name you added
            string getSettingUrl = $"{baseFunctionUrl}/settings/{AWAY_TIMEOUT_SETTING_NAME}?code={functionKey}";
            //string getSettingUrl = $"{baseFunctionUrl}/settings/{AWAY_TIMEOUT_SETTING_NAME}"; // Use this if AuthLevel is Function and key is in URL, or Anonymous

            //_log.LogInformation($"Attempting to GET setting from: {getSettingUrl}"); // Use relative path in log if preferred

            try
            {
                var response = await _httpRetryPolicy.ExecuteAsync(() => _httpClient.GetAsync(getSettingUrl));

                if (response.IsSuccessStatusCode)
                {
                    string value = await response.Content.ReadAsStringAsync();
                    _log.LogInformation($"Retrieved {AWAY_TIMEOUT_SETTING_NAME}: {value}");
                    return value;
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _log.LogError($"Failed to get {AWAY_TIMEOUT_SETTING_NAME}. Status: {response.StatusCode}. Content: {errorContent}");
                    // Return a default value or null to indicate failure/not found
                    return "3"; // Default to 3 minutes if fetch fails
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Exception occurred while getting {AWAY_TIMEOUT_SETTING_NAME}.");
                return "3"; // Default to 3 minutes on exception
            }
        }

        /// <summary>
        /// Updates the Away Timeout setting via the Azure Function.
        /// </summary>
        /// <param name="minutes">The new timeout value in minutes (as a string).</param>
        /// <returns>True if successful, false otherwise.</returns>
        public async Task<bool> UpdateAwayTimeoutSettingAsync(string minutes)
        {
            await _azureHandler.InitializationTask; // Wait for Azure keys to be loaded
            // Assuming UpdateSetting requires a function key, adjust if anonymous
            var functionKey = _azureHandler._settings.DefaultHostKey; // Use the property name you added
            string updateSettingUrl = $"{baseFunctionUrl}/settings/{AWAY_TIMEOUT_SETTING_NAME}?code={functionKey}";
            //string updateSettingUrl = $"{baseFunctionUrl}/settings/{AWAY_TIMEOUT_SETTING_NAME}"; // Use this if AuthLevel is Function and key is in URL, or Anonymous

            _log.LogInformation($"Attempting to POST setting to: {updateSettingUrl}"); // Use relative path in log if preferred

            try
            {
                // Construct JSON payload: {"value": "..."}
                var payload = new { value = minutes };
                var jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var response = await _httpRetryPolicy.ExecuteAsync(() => _httpClient.PostAsync(updateSettingUrl, content));

                if (response.IsSuccessStatusCode)
                {
                    _log.LogInformation($"Successfully updated {AWAY_TIMEOUT_SETTING_NAME} to {minutes}.");
                    return true;
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _log.LogError($"Failed to update {AWAY_TIMEOUT_SETTING_NAME}. Status: {response.StatusCode}. Content: {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Exception occurred while updating {AWAY_TIMEOUT_SETTING_NAME}.");
                return false;
            }
        }

        public async Task<bool> ManageReportPermissionsAsync(List<ReportPermission> permissions)
        {
            // Check if the input list is null or empty
            if (permissions == null || !permissions.Any())
            {
                _log.LogWarning("ManageReportPermissionsAsync called with null or empty list. No action taken.");
                return true; // Or false depending on how you want to handle empty input
            }

            await _azureHandler.InitializationTask;
            // *** USE DefaultHostKey *** (As confirmed previously)
            var functionKey = _azureHandler._settings.DefaultHostKey;
            if (string.IsNullOrEmpty(functionKey))
            {
                _log.LogError("DefaultHostKey is missing. Cannot manage report permissions.");
                return false;
            }

            // The endpoint URL remains the same
            string managePermissionUrl = $"{baseFunctionUrl}/permissions/manage?code={functionKey}";

            // Determine action for logging based on the first item (assuming batch has same action type)
            string actionType = permissions.First().Remove ? "remove" : "add/update";
            _log.LogInformation($"Attempting to {actionType} {permissions.Count} report permissions at: {managePermissionUrl}");

            try
            {
                // --- >> CHANGE START: Serialize the LIST << ---
                // Configure serializer options if needed (e.g., for enums, though not used in ReportPermission)
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                // Serialize the entire list of permissions
                string jsonPayload = JsonSerializer.Serialize(permissions, options);
                // --- >> CHANGE END: Serialize the LIST << ---

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Use the existing _httpRetryPolicy
                var response = await _httpRetryPolicy.ExecuteAsync(() => _httpClient.PostAsync(managePermissionUrl, content));

                if (response.IsSuccessStatusCode)
                {
                    _log.LogInformation($"Successfully sent batch request to {actionType} {permissions.Count} report permissions.");
                    return true;
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _log.LogError($"Failed to manage report permissions batch. Status: {response.StatusCode}. Content: {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Exception occurred while managing report permissions batch ({actionType}).");
                return false;
            }
        }

        public async Task<List<ReportPermission>> GetUserReportPermissionsAsync(string userId)
        {
            await _azureHandler.InitializationTask;
            // *** USE DefaultHostKey ***
            var functionKey = _azureHandler._settings.DefaultHostKey;
            if (string.IsNullOrEmpty(functionKey))
            {
                _log.LogError("DefaultHostKey is missing. Cannot get report permissions.");
                return null; // Indicate error: cannot proceed without key
            }

            string encodedUserId = Uri.EscapeDataString(userId);
            // Assuming a GET endpoint for querying
            string getPermissionsUrl = $"{baseFunctionUrl}/permissions/query?userId={encodedUserId}&code={functionKey}";

            _log.LogInformation($"Attempting to GET report permissions for user '{userId}' from: {getPermissionsUrl}");

            try
            {
                // No retry policy on GET, or one that excludes 404 Not Found
                var response = await _httpClient.GetAsync(getPermissionsUrl);

                if (response.IsSuccessStatusCode)
                {
                    string jsonContent = await response.Content.ReadAsStringAsync();
                    // Use JsonSerializerOptions for case-insensitive property matching if needed
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var permissions = JsonSerializer.Deserialize<List<ReportPermission>>(jsonContent, options);
                    _log.LogInformation($"Successfully retrieved {permissions?.Count ?? 0} permissions for user '{userId}'.");
                    return permissions ?? new List<ReportPermission>(); // Return empty list if deserialization results in null
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _log.LogInformation($"No permissions found for user '{userId}'. Returning empty list.");
                    return new List<ReportPermission>(); // Return empty list for 404
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _log.LogError($"Failed to get report permissions for user '{userId}'. Status: {response.StatusCode}. Content: {errorContent}");
                    return null; // Indicate error for other non-success status codes
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Exception occurred while getting report permissions for user '{userId}'.");
                return null; // Indicate error on exception
            }
        }

        // Inside EdgeAlertService.cs

        public async Task<List<SummarizedReportData>> GetSummarizedReportDataAsync(DateTime startDate, DateTime endDate, string filterUser, string filterOds)
        {
            await _azureHandler.InitializationTask;
            var functionKey = _azureHandler._settings.DefaultHostKey; // Use default key
            if (string.IsNullOrEmpty(functionKey))
            {
                _log.LogError("DefaultHostKey is missing. Cannot get summarized report data.");
                return null;
            }

            // Build query string parameters
            var queryParams = new Dictionary<string, string>
    {
        { "code", functionKey },
        { "startDate", startDate.ToString("yyyy-MM-dd") },
        { "endDate", endDate.ToString("yyyy-MM-dd") }
    };
            if (!string.IsNullOrWhiteSpace(filterUser) && filterUser != "All")
            {
                queryParams.Add("user", filterUser);
            }
            if (!string.IsNullOrWhiteSpace(filterOds) && filterOds != "All")
            {
                queryParams.Add("ods", filterOds);
            }

            var encodedParams = new FormUrlEncodedContent(queryParams);
            string queryString = await encodedParams.ReadAsStringAsync();

            string requestUrl = $"{baseFunctionUrl}/GetSummarizedReportData?{queryString}";
            //_log.LogInformation($"Attempting to GET summarized report data from: {requestUrl}");

            try
            {
                // Use HttpRequestMessage to add the header
                using (var request = new HttpRequestMessage(HttpMethod.Get, requestUrl))
                {
                    // Add the required header using the existing _userId field
                    // Ensure _userId is set correctly before this call (it's set in NegotiateAsync)
                    if (string.IsNullOrEmpty(_userId))
                    {
                        _log.LogError("Cannot get summarized report data: _userId is not set.");
                        return null; // Or handle appropriately
                    }
                    request.Headers.Add("X-Authenticated-User-Id", _userId);

                    // Send the request using SendAsync
                    var response = await _httpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonContent = await response.Content.ReadAsStringAsync();
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var data = JsonSerializer.Deserialize<List<SummarizedReportData>>(jsonContent, options);
                        _log.LogInformation($"Successfully retrieved {data?.Count ?? 0} summarized records.");
                        return data ?? new List<SummarizedReportData>();
                    }
                    else
                    {
                        string errorContent = await response.Content.ReadAsStringAsync();
                        _log.LogError($"Failed to get summarized report data. Status: {response.StatusCode}. Content: {errorContent}");
                        return null;
                    }
                } // using statement ensures request is disposed
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Exception occurred while getting summarized report data.");
                return null;
            }
        }

        public async Task<List<DetailedReportData>> GetDetailedReportDataAsync(DateTime startDate, DateTime endDate, string filterUser, string filterMachine, string filterOds)
        {
            await _azureHandler.InitializationTask;
            var functionKey = _azureHandler._settings.DefaultHostKey; // Use default key
            if (string.IsNullOrEmpty(functionKey))
            {
                _log.LogError("DefaultHostKey is missing. Cannot get detailed report data.");
                return null;
            }

            // Build query string parameters
            var queryParams = new Dictionary<string, string>
    {
        { "code", functionKey },
        { "startDate", startDate.ToString("yyyy-MM-dd") },
        { "endDate", endDate.ToString("yyyy-MM-dd") }
    };
            if (!string.IsNullOrWhiteSpace(filterUser) && filterUser != "All")
            {
                queryParams.Add("user", filterUser);
            }
            if (!string.IsNullOrWhiteSpace(filterMachine))
            {
                queryParams.Add("machine", filterMachine);
            }
            if (!string.IsNullOrWhiteSpace(filterOds) && filterOds != "All")
            {
                queryParams.Add("ods", filterOds);
            }

            var encodedParams = new FormUrlEncodedContent(queryParams);
            string queryString = await encodedParams.ReadAsStringAsync();

            string requestUrl = $"{baseFunctionUrl}/GetDetailedReportData?{queryString}";
            //_log.LogInformation($"Attempting to GET detailed report data from: {requestUrl}");
            try
            {
                // Use HttpRequestMessage to add the header
                using (var request = new HttpRequestMessage(HttpMethod.Get, requestUrl))
                {
                    // Add the required header using the existing _userId field
                    // Ensure _userId is set correctly before this call (it's set in NegotiateAsync)
                    if (string.IsNullOrEmpty(_userId))
                    {
                        _log.LogError("Cannot get detailed report data: _userId is not set.");
                        return null; // Or handle appropriately
                    }
                    request.Headers.Add("X-Authenticated-User-Id", _userId);

                    // Send the request using SendAsync
                    var response = await _httpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonContent = await response.Content.ReadAsStringAsync();
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var data = JsonSerializer.Deserialize<List<DetailedReportData>>(jsonContent, options);
                        _log.LogInformation($"Successfully retrieved {data?.Count ?? 0} detailed records.");
                        return data ?? new List<DetailedReportData>();
                    }
                    else
                    {
                        string errorContent = await response.Content.ReadAsStringAsync();
                        _log.LogError($"Failed to get detailed report data. Status: {response.StatusCode}. Content: {errorContent}");
                        return null;
                    }
                } // using statement ensures request is disposed
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Exception occurred while getting detailed report data.");
                return null;
            }
        }

        public async Task<List<string>> GetPermittedOdsAsync()
        {
            await _azureHandler.InitializationTask; // Ensure keys are loaded
            var functionKey = _azureHandler._settings.DefaultHostKey; // Use appropriate key
            if (string.IsNullOrEmpty(functionKey))
            {
                _log.LogError("DefaultHostKey is missing. Cannot get permitted ODS.");
                return null;
            }
            if (string.IsNullOrEmpty(_userId))
            {
                _log.LogError("Cannot get permitted ODS: _userId is not set.");
                return null;
            }

            string requestUrl = $"{baseFunctionUrl}/GetPermittedOdsForUser?code={functionKey}";
            //_log.LogInformation($"Attempting to GET permitted ODS from: {requestUrl}");

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, requestUrl))
                {
                    request.Headers.Add("X-Authenticated-User-Id", _userId);
                    var response = await _httpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonContent = await response.Content.ReadAsStringAsync();
                        var odsCodes = JsonSerializer.Deserialize<List<string>>(jsonContent);
                        _log.LogInformation($"Successfully retrieved {odsCodes?.Count ?? 0} permitted ODS codes.");
                        return odsCodes ?? new List<string>();
                    }
                    else
                    {
                        string errorContent = await response.Content.ReadAsStringAsync();
                        _log.LogError($"Failed to get permitted ODS. Status: {response.StatusCode}. Content: {errorContent}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Exception occurred while getting permitted ODS.");
                return null;
            }
        }

        public async Task<List<string>> GetPermittedUsersForOdsAsync(string targetOds)
        {
            await _azureHandler.InitializationTask;
            var functionKey = _azureHandler._settings.DefaultHostKey;
            if (string.IsNullOrEmpty(functionKey) || string.IsNullOrEmpty(targetOds))
            {
                _log.LogError("DefaultHostKey or targetOds is missing. Cannot get permitted users.");
                return null;
            }
            if (string.IsNullOrEmpty(_userId))
            {
                _log.LogError("Cannot get permitted users: _userId is not set.");
                return null;
            }

            // Encode targetOds for the query string
            string encodedTargetOds = Uri.EscapeDataString(targetOds);
            string requestUrl = $"{baseFunctionUrl}/GetPermittedUsersForOds?code={functionKey}&targetOds={encodedTargetOds}";
            //_log.LogInformation($"Attempting to GET permitted users for ODS {targetOds} from: {requestUrl}");

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, requestUrl))
                {
                    request.Headers.Add("X-Authenticated-User-Id", _userId);
                    var response = await _httpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonContent = await response.Content.ReadAsStringAsync();
                        var userNames = JsonSerializer.Deserialize<List<string>>(jsonContent);
                        _log.LogInformation($"Successfully retrieved {userNames?.Count ?? 0} permitted users for ODS {targetOds}.");
                        return userNames ?? new List<string>();
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        _log.LogWarning($"Access denied to get users for ODS {targetOds}. User might not have permission.");
                        return new List<string>(); // Return empty list if forbidden
                    }
                    else
                    {
                        string errorContent = await response.Content.ReadAsStringAsync();
                        _log.LogError($"Failed to get permitted users for ODS {targetOds}. Status: {response.StatusCode}. Content: {errorContent}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Exception occurred while getting permitted users for ODS {targetOds}.");
                return null;
            }
        }

        public async Task<bool> CheckHasAnyReportAccessAsync()
        {
            // Reuse GetPermittedOdsAsync which fetches permitted ODS list from backend
            var permittedOds = await GetPermittedOdsAsync();
            // User has access if the list is not null and contains at least one ODS code
            bool hasAccess = permittedOds != null && permittedOds.Any();
            _log.LogInformation($"CheckHasAnyReportAccessAsync for user '{_userId}': {hasAccess}"); // Logging
            return hasAccess;
        }

        /// <summary>
        /// Gets the effective enablement status for a specific feature, user, and ODS code from the backend.
        /// Uses the client-side EffectiveFeatureSettingResult DTO for deserialization.
        /// </summary>
        public async Task<bool> GetEffectiveFeatureSettingAsync(string featureName, string userName, string odsCode)
        {
            // Ensure Azure keys are loaded if needed (e.g., for function key)
            // Use a try-catch block or check InitializationTask status if necessary
            // For simplicity, assume InitializationTask is awaited elsewhere or handled.
            // If the endpoint is anonymous, key retrieval might not be needed.
            string functionKey = null;
            try
            {
                await _azureHandler.InitializationTask; // Ensure keys are available
                functionKey = _azureHandler._settings.DefaultHostKey; // Or specific key
            }
            catch (Exception keyEx)
            {
                _log.LogError(keyEx, "GetEffectiveFeatureSettingAsync: Failed to get Azure function key during initialization.");
                // Fallback to disabled if keys fail
                return false;
            }


            if (string.IsNullOrEmpty(functionKey)) // Check AFTER attempting retrieval
            {
                _log.LogError("GetEffectiveFeatureSettingAsync: DefaultHostKey is missing after initialization. Cannot query setting.");
                return false; // Default to disabled if key missing
            }
            if (string.IsNullOrWhiteSpace(featureName) || string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(odsCode))
            {
                _log.LogError("GetEffectiveFeatureSettingAsync: Missing required parameters (featureName, userName, odsCode).");
                return false; // Default to disabled if params missing
            }

            // Encode parameters for URL
            string encodedFeature = Uri.EscapeDataString(featureName);
            string encodedUser = Uri.EscapeDataString(userName);
            string encodedOds = Uri.EscapeDataString(odsCode);

            // Construct the URL for the new Azure Function endpoint
            // Using route defined in Phase 1: featuresettings/effective
            string requestUrl = $"{baseFunctionUrl}/featuresettings/effective?code={functionKey}&featureName={encodedFeature}&userName={encodedUser}&odsCode={encodedOds}";

            //_log.LogInformation($"Attempting GET to query effective setting: {requestUrl}");

            HttpResponseMessage response = null;
            try
            {
                // Use simple GET request
                response = await _httpClient.GetAsync(requestUrl); // Consider adding CancellationToken if needed

                if (response.IsSuccessStatusCode)
                {
                    string jsonContent = await response.Content.ReadAsStringAsync();
                    _log.LogDebug($"GetEffectiveFeatureSettingAsync response JSON: {jsonContent}");

                    // *** Use the Client-Side DTO for Deserialization ***
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var resultDto = JsonSerializer.Deserialize<EffectiveFeatureSettingResult>(jsonContent, options);

                    if (resultDto != null)
                    {
                        _log.LogInformation($"Effective setting for Feature='{featureName}', User='{userName}', ODS='{odsCode}' is {resultDto.EffectiveIsEnabled}.");
                        return resultDto.EffectiveIsEnabled;
                    }
                    else
                    {
                        _log.LogError($"Failed to deserialize response JSON into EffectiveFeatureSettingResult. JSON: {jsonContent}");
                        return false; // Default to disabled on deserialization failure
                    }
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _log.LogError($"Failed to get effective setting. Status: {response.StatusCode}. Content: {errorContent}");
                    return false; // Default to disabled on API error
                }
            }
            catch (JsonException jsonEx)
            {
                _log.LogError(jsonEx, "JSON Error deserializing effective feature setting response.");
                return false; // Default to disabled on JSON error
            }
            catch (HttpRequestException httpEx)
            {
                _log.LogError(httpEx, "HTTP Request Error getting effective feature setting.");
                return false; // Default to disabled on network error
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unexpected exception occurred while getting effective feature setting.");
                return false; // Default to disabled on unexpected exception
            }
            finally
            {
                response?.Dispose(); // Ensure response is disposed
            }
        }

        /// <summary>
        /// Gets all feature settings from the backend for populating the client cache.
        /// Calls the GetAllFeatureSettings Azure Function.
        /// </summary>
        /// <returns>A list of FeatureSettingInfo objects, or null on failure.</returns>
        public async Task<List<FeatureSettingInfo>> GetAllFeatureSettingsAsync() // Removed featureName parameter - fetching all relevant
        {
            // Ensure Azure keys are loaded if needed
            string functionKey = null;
            try
            {
                await _azureHandler.InitializationTask;
                functionKey = _azureHandler._settings.DefaultHostKey; // Use default key for this function
            }
            catch (Exception keyEx)
            {
                _log.LogError(keyEx, "GetAllFeatureSettingsAsync: Failed to get Azure function key during initialization.");
                return null; // Cannot proceed without key
            }

            if (string.IsNullOrEmpty(functionKey))
            {
                _log.LogError("GetAllFeatureSettingsAsync: DefaultHostKey is missing after initialization. Cannot query settings.");
                return null; // Return null if key missing
            }

            // Construct the URL for the GetAllFeatureSettings Azure Function
            // Using route defined in Phase 1: featuresettings/getall
            // This endpoint might require adjustments if you need to specify *which* features to get,
            // but for now, let's assume it returns all settings relevant to the client cache.
            // If it requires a featureName, you'll need to adjust the call or fetch multiple times.
            string requestUrl = $"{baseFunctionUrl}/featuresettings/getall?code={functionKey}"; // Removed featureName query param

            //_log.LogInformation($"Attempting GET to query all feature settings: {requestUrl}");

            HttpResponseMessage response = null;
            try
            {
                // Use simple GET request
                response = await _httpClient.GetAsync(requestUrl); // Consider CancellationToken

                if (response.IsSuccessStatusCode)
                {
                    string jsonContent = await response.Content.ReadAsStringAsync();
                    //_log.LogDebug($"GetAllFeatureSettingsAsync response JSON: {jsonContent}");

                    // Use the Client-Side DTO for Deserialization
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var resultDtoList = JsonSerializer.Deserialize<List<FeatureSettingInfo>>(jsonContent, options);

                    if (resultDtoList != null)
                    {
                        _log.LogInformation($"Successfully retrieved {resultDtoList.Count} feature setting entries from backend.");
                        return resultDtoList;
                    }
                    else
                    {
                        _log.LogError($"Failed to deserialize response JSON into List<FeatureSettingInfo>. JSON: {jsonContent}");
                        return new List<FeatureSettingInfo>(); // Return empty list on deserialization failure
                    }
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _log.LogError($"Failed to get all feature settings. Status: {response.StatusCode}. Content: {errorContent}");
                    return null; // Return null on API error
                }
            }
            catch (JsonException jsonEx)
            {
                _log.LogError(jsonEx, "JSON Error deserializing all feature settings response.");
                return null; // Return null on JSON error
            }
            catch (HttpRequestException httpEx)
            {
                _log.LogError(httpEx, "HTTP Request Error getting all feature settings.");
                return null; // Return null on network error
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unexpected exception occurred while getting all feature settings.");
                return null; // Return null on unexpected exception
            }
            finally
            {
                response?.Dispose(); // Ensure response is disposed
            }
        }

        /// <summary>
        /// Sets or updates a specific feature setting via the backend API.
        /// </summary>
        public async Task<bool> SetFeatureSettingAsync(string featureName, string scopeIdentifier, bool isEnabled)
        {
            string functionKey = null;
            try
            {
                await _azureHandler.InitializationTask;
                functionKey = _azureHandler._settings.DefaultHostKey; // Use appropriate key
                if (string.IsNullOrEmpty(functionKey))
                {
                    _log.LogError("SetFeatureSettingAsync: Function key is missing. Cannot update setting.");
                    return false;
                }
            }
            catch (Exception keyEx)
            {
                _log.LogError(keyEx, "SetFeatureSettingAsync: Failed to get Azure function key during initialization.");
                return false;
            }

            // Construct the URL for the SetFeatureSetting Azure Function
            // Using route defined in Phase 1: featuresettings/set
            string requestUrl = $"{baseFunctionUrl}/featuresettings/set?code={functionKey}";
            //_log.LogInformation($"Attempting POST to set setting: {requestUrl} for Feature='{featureName}', Scope='{scopeIdentifier}', IsEnabled={isEnabled}");

            HttpResponseMessage response = null;
            try
            {
                // Create the JSON payload
                var payload = new { featureName, scopeIdentifier, isEnabled };
                string jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Use retry policy for the POST request
                response = await _httpRetryPolicy.ExecuteAsync(() => _httpClient.PostAsync(requestUrl, content));

                if (response.IsSuccessStatusCode)
                {
                    _log.LogInformation($"Successfully set setting Feature='{featureName}', Scope='{scopeIdentifier}' to {isEnabled}.");
                    return true;
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _log.LogError($"Failed to set feature setting. Status: {response.StatusCode}. Content: {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Exception occurred while setting feature setting Feature='{featureName}', Scope='{scopeIdentifier}'.");
                return false;
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        /// Deletes a specific feature setting via the backend API.
        /// </summary>
        public async Task<bool> DeleteFeatureSettingAsync(string featureName, string scopeIdentifier)
        {
            string functionKey = null;
            try
            {
                await _azureHandler.InitializationTask;
                functionKey = _azureHandler._settings.DefaultHostKey; // Use appropriate key
                if (string.IsNullOrEmpty(functionKey))
                {
                    _log.LogError("DeleteFeatureSettingAsync: Function key is missing. Cannot delete setting.");
                    return false;
                }
            }
            catch (Exception keyEx)
            {
                _log.LogError(keyEx, "DeleteFeatureSettingAsync: Failed to get Azure function key during initialization.");
                return false;
            }

            // Construct the URL for the DeleteFeatureSetting Azure Function
            // Using route defined in Phase 1: featuresettings/delete
            string requestUrl = $"{baseFunctionUrl}/featuresettings/delete?code={functionKey}";
            //_log.LogInformation($"Attempting DELETE to delete setting: {requestUrl} for Feature='{featureName}', Scope='{scopeIdentifier}'");

            HttpResponseMessage response = null;
            try
            {
                // Create the JSON payload required by the backend function
                var payload = new { featureName, scopeIdentifier };
                string jsonPayload = JsonSerializer.Serialize(payload);
                var request = new HttpRequestMessage(HttpMethod.Delete, requestUrl) // Use DELETE verb
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };

                // Send the request - Retries might not be suitable for DELETE if non-idempotent issues occur
                // For simplicity, we'll send it directly for now. Consider implications if retries are needed.
                response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode) // Includes 200 OK, 204 No Content potentially
                {
                    _log.LogInformation($"Successfully deleted setting (or it didn't exist) Feature='{featureName}', Scope='{scopeIdentifier}'. Status: {response.StatusCode}");
                    return true; // Treat success or not found as success for the delete operation
                }
                else if (response.StatusCode == HttpStatusCode.NotFound) // Explicitly handle 404 as success
                {
                    _log.LogWarning($"Setting not found for deletion Feature='{featureName}', Scope='{scopeIdentifier}'. Considered successful.");
                    return true;
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _log.LogError($"Failed to delete feature setting. Status: {response.StatusCode}. Content: {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Exception occurred while deleting feature setting Feature='{featureName}', Scope='{scopeIdentifier}'.");
                return false;
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        /// Retrieves a list of distinct, available feature names from the backend.
        /// </summary>
        public async Task<List<string>> GetAvailableFeaturesAsync()
        {
            string functionKey = null;
            try
            {
                await _azureHandler.InitializationTask;
                functionKey = _azureHandler._settings.DefaultHostKey; // Use appropriate key
                if (string.IsNullOrEmpty(functionKey))
                {
                    _log.LogError("GetAvailableFeaturesAsync: Function key is missing. Cannot list features.");
                    return null; // Return null to indicate failure
                }
            }
            catch (Exception keyEx)
            {
                _log.LogError(keyEx, "GetAvailableFeaturesAsync: Failed to get Azure function key during initialization.");
                return null;
            }

            // Construct the URL for the ListFeatures Azure Function
            // Using route defined in Phase 3b Step 1: featuresettings/listfeatures
            string requestUrl = $"{baseFunctionUrl}/featuresettings/listfeatures?code={functionKey}";
            //_log.LogInformation($"Attempting GET to list features: {requestUrl}");

            HttpResponseMessage response = null;
            try
            {
                // Use retry policy for the GET request
                response = await _httpRetryPolicy.ExecuteAsync(() => _httpClient.GetAsync(requestUrl));

                if (response.IsSuccessStatusCode)
                {
                    string jsonContent = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    List<string> featureNames = JsonSerializer.Deserialize<List<string>>(jsonContent, options);
                    _log.LogInformation($"Successfully retrieved {featureNames?.Count ?? 0} available feature names.");
                    return featureNames ?? new List<string>(); // Return empty list if deserialization yields null
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _log.LogError($"Failed to retrieve available features. Status: {response.StatusCode}. Content: {errorContent}");
                    return null; // Return null on error status code
                }
            }
            catch (JsonException jsonEx)
            {
                _log.LogError(jsonEx, "Exception occurred while deserializing feature list.");
                return null;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Exception occurred while retrieving available features.");
                return null; // Return null on other exceptions
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        /// Calls the backend Azure Function to set the enabled state for a feature across ALL ODS scopes.
        /// </summary>
        /// <param name="featureName">The name of the feature.</param>
        /// <param name="isEnabled">The desired state (true or false).</param>
        /// <returns>True if the backend operation was successful, false otherwise.</returns>
        public async Task<bool> SetBulkOdsSettingsAsync(string featureName, bool isEnabled)
        {
            string functionKey = null;
            try
            {
                // Ensure Azure Handler is initialized and get the key
                await _azureHandler.InitializationTask;
                functionKey = _azureHandler._settings.DefaultHostKey; // Assuming DefaultHostKey is appropriate
                if (string.IsNullOrEmpty(functionKey))
                {
                    _log.LogError("SetBulkOdsSettingsAsync: Function key (DefaultHostKey) is missing. Cannot perform bulk update.");
                    return false;
                }
            }
            catch (Exception keyEx)
            {
                _log.LogError(keyEx, "SetBulkOdsSettingsAsync: Failed to get Azure function key during initialization.");
                return false;
            }

            // Construct the URL for the SetBulkOdsSettings Azure Function
            string requestUrl = $"{baseFunctionUrl}/featuresettings/setbulkods?code={functionKey}";
            string stateText = isEnabled ? "ON" : "OFF";
            //_log.LogInformation($"Attempting POST to bulk set ODS settings: {requestUrl} for Feature='{featureName}' to {stateText}");

            HttpResponseMessage response = null;
            try
            {
                // Create the JSON payload: { "featureName": "...", "isEnabled": true/false }
                var payload = new { featureName, isEnabled };
                string jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Use the existing retry policy for the POST request
                response = await _httpRetryPolicy.ExecuteAsync(() => _httpClient.PostAsync(requestUrl, content));

                if (response.IsSuccessStatusCode)
                {
                    _log.LogInformation($"Successfully requested bulk ODS update for Feature='{featureName}' to {stateText}.");
                    return true;
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _log.LogError($"Failed to request bulk ODS update for Feature='{featureName}'. Status: {response.StatusCode}. Content: {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Exception occurred during bulk ODS update request for Feature='{featureName}'.");
                return false;
            }
            finally
            {
                response?.Dispose(); // Ensure response is disposed
            }
        }

        /// <summary>
        /// Calls the backend Azure Function to set the enabled state for a feature across ALL USER scopes.
        /// </summary>
        /// <param name="featureName">The name of the feature.</param>
        /// <param name="isEnabled">The desired state (true or false).</param>
        /// <returns>True if the backend operation was successful, false otherwise.</returns>
        public async Task<bool> SetBulkUserSettingsAsync(string featureName, bool isEnabled)
        {
            string functionKey = null;
            try
            {
                // Ensure Azure Handler is initialized and get the key
                await _azureHandler.InitializationTask;
                functionKey = _azureHandler._settings.DefaultHostKey; // Assuming DefaultHostKey is appropriate
                if (string.IsNullOrEmpty(functionKey))
                {
                    _log.LogError("SetBulkUserSettingsAsync: Function key (DefaultHostKey) is missing. Cannot perform bulk update.");
                    return false;
                }
            }
            catch (Exception keyEx)
            {
                _log.LogError(keyEx, "SetBulkUserSettingsAsync: Failed to get Azure function key during initialization.");
                return false;
            }

            // Construct the URL for the SetBulkUserSettings Azure Function
            string requestUrl = $"{baseFunctionUrl}/featuresettings/setbulkusers?code={functionKey}";
            string stateText = isEnabled ? "ON" : "OFF";
            //_log.LogInformation($"Attempting POST to bulk set USER settings: {requestUrl} for Feature='{featureName}' to {stateText}");

            HttpResponseMessage response = null;
            try
            {
                // Create the JSON payload: { "featureName": "...", "isEnabled": true/false }
                var payload = new { featureName, isEnabled };
                string jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Use the existing retry policy for the POST request
                response = await _httpRetryPolicy.ExecuteAsync(() => _httpClient.PostAsync(requestUrl, content));

                if (response.IsSuccessStatusCode)
                {
                    _log.LogInformation($"Successfully requested bulk USER update for Feature='{featureName}' to {stateText}.");
                    return true;
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _log.LogError($"Failed to request bulk USER update for Feature='{featureName}'. Status: {response.StatusCode}. Content: {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Exception occurred during bulk USER update request for Feature='{featureName}'.");
                return false;
            }
            finally
            {
                response?.Dispose(); // Ensure response is disposed
            }
        }

        /// <summary>
        /// Calls the backend Azure Function to delete all ODS-specific overrides for a feature.
        /// </summary>
        /// <param name="featureName">The name of the feature.</param>
        /// <returns>True if the backend operation was successful, false otherwise.</returns>
        public async Task<bool> DeleteAllOdsOverridesAsync(string featureName)
        {
            string functionKey = null;
            try
            {
                await _azureHandler.InitializationTask;
                functionKey = _azureHandler._settings.DefaultHostKey;
                if (string.IsNullOrEmpty(functionKey))
                {
                    _log.LogError("DeleteAllOdsOverridesAsync: Function key (DefaultHostKey) is missing. Cannot perform bulk delete.");
                    return false;
                }
            }
            catch (Exception keyEx)
            {
                _log.LogError(keyEx, "DeleteAllOdsOverridesAsync: Failed to get Azure function key during initialization.");
                return false;
            }

            string requestUrl = $"{baseFunctionUrl}/featuresettings/deletebulkods?code={functionKey}";
            //_log.LogInformation($"Attempting DELETE to bulk delete ODS overrides: {requestUrl} for Feature='{featureName}'");

            HttpResponseMessage response = null;
            try
            {
                // Prepare payload outside the lambda (can be reused)
                var payload = new { featureName };
                string jsonPayload = JsonSerializer.Serialize(payload);

                // Use the existing retry policy
                // *** Move Request and Content creation INSIDE the lambda ***
                response = await _httpRetryPolicy.ExecuteAsync(async () => {
                    // Create new content for each attempt (safer in case of disposal)
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    // Create new request message for each attempt
                    var request = new HttpRequestMessage(HttpMethod.Delete, requestUrl)
                    {
                        Content = content
                    };
                    // Send the new request
                    return await _httpClient.SendAsync(request);
                });


                if (response.IsSuccessStatusCode)
                {
                    _log.LogInformation($"Successfully requested bulk ODS override deletion for Feature='{featureName}'.");
                    return true;
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _log.LogError($"Failed to request bulk ODS override deletion for Feature='{featureName}'. Status: {response.StatusCode}. Content: {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Exception occurred during bulk ODS override deletion request for Feature='{featureName}'.");
                return false;
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        /// Calls the backend Azure Function to delete all User-specific overrides for a feature.
        /// </summary>
        /// <param name="featureName">The name of the feature.</param>
        /// <returns>True if the backend operation was successful, false otherwise.</returns>
        public async Task<bool> DeleteAllUserOverridesAsync(string featureName)
        {
            string functionKey = null;
            try
            {
                await _azureHandler.InitializationTask;
                functionKey = _azureHandler._settings.DefaultHostKey;
                if (string.IsNullOrEmpty(functionKey))
                {
                    _log.LogError("DeleteAllUserOverridesAsync: Function key (DefaultHostKey) is missing. Cannot perform bulk delete.");
                    return false;
                }
            }
            catch (Exception keyEx)
            {
                _log.LogError(keyEx, "DeleteAllUserOverridesAsync: Failed to get Azure function key during initialization.");
                return false;
            }

            string requestUrl = $"{baseFunctionUrl}/featuresettings/deletebulkusers?code={functionKey}";
            //_log.LogInformation($"Attempting DELETE to bulk delete USER overrides: {requestUrl} for Feature='{featureName}'");

            HttpResponseMessage response = null;
            try
            {
                // Prepare payload outside the lambda (can be reused)
                var payload = new { featureName };
                string jsonPayload = JsonSerializer.Serialize(payload);

                // Use the existing retry policy
                // *** Move Request and Content creation INSIDE the lambda ***
                response = await _httpRetryPolicy.ExecuteAsync(async () => {
                    // Create new content for each attempt (safer in case of disposal)
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    // Create new request message for each attempt
                    var request = new HttpRequestMessage(HttpMethod.Delete, requestUrl)
                    {
                        Content = content
                    };
                    // Send the new request
                    return await _httpClient.SendAsync(request);
                });


                if (response.IsSuccessStatusCode)
                {
                    _log.LogInformation($"Successfully requested bulk USER override deletion for Feature='{featureName}'.");
                    return true;
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _log.LogError($"Failed to request bulk USER override deletion for Feature='{featureName}'. Status: {response.StatusCode}. Content: {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Exception occurred during bulk USER override deletion request for Feature='{featureName}'.");
                return false;
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    public class NegotiateData
    {
        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; }
    }

    public class UpdateUserResponse
    {
        [JsonPropertyName("userName")]
        public string UserName { get; set; }

        [JsonPropertyName("isBlocked")]
        public bool IsBlocked { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }
    }
}
