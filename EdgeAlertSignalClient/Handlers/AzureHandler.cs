using Azure.Core;
using Azure.Identity; // Keep this using for ClientSecretCredential
using Azure.Security.KeyVault.Secrets;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using EdgeAlertSignalClient.Models;
using Azure; // Assuming AzureSettings definition might move here or stay

namespace EdgeAlertSignalClient.Handlers
{
    // Define AzureSettings within or outside the class as preferred
    public class AzureSettings
    {
        public string? DatabaseConnectionString { get; set; }
        public string? SendMessageFunctionKey { get; set; }
        public string? UpdateUserFunctionKey { get; set; }
        public string? DisconnectUserFunctionKey { get; set; }
        public string? RespondToAlertFunctionKey { get; set; }
        public string? DefaultHostKey { get; set; }
        // Add other settings as needed
    }

    public partial class AzureHandler : ObservableObject
    {
        // Key Vault details - these can be overridden by environment variables
        private readonly string KeyVaultUriString;
        private readonly string TenantId;
        private readonly string ClientId;
        private readonly string ClientSecret;


        // Nullable settings object, only populated on success
        public AzureSettings? _settings { get; private set; }

        private readonly ILogger<AzureHandler> _log;
        private Lazy<Task<bool>> _initializationLazyTask; // Task now returns bool (success/failure)
        private bool _isInitializedSuccessfully = false;
        private readonly SemaphoreSlim _initSemaphore = new SemaphoreSlim(1, 1); // Protect initialization logic

        // Public Task to await initialization completion and check success
        public Task<bool> InitializationTask => _initializationLazyTask.Value;

        public AzureHandler(ILogger<AzureHandler> log)
        {
            _log = log;
            
            // Load configuration from environment variables with fallbacks
            KeyVaultUriString = Environment.GetEnvironmentVariable("AZURE_KEYVAULT_URI") 
                ?? "https://edgestoragevault.vault.azure.net/";
            
            TenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID") 
                ?? "4ab91383-3140-4414-942f-74e6dfe5035f";
            
            ClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID") 
                ?? "187b13e7-ad4d-486e-86a1-4ae15dd5b65d";
            
            ClientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET") 
                ?? "";  // No default for security reasons
            
            if (string.IsNullOrEmpty(ClientSecret))
            {
                _log.LogWarning("Azure Client Secret not configured. Set AZURE_CLIENT_SECRET environment variable.");
            }
            
            // Setup Lazy Task to call the internal initialization method
            _initializationLazyTask = new Lazy<Task<bool>>(InitializeInternalAsync, LazyThreadSafetyMode.ExecutionAndPublication);
            _log.LogInformation("AzureHandler created. Initialization task setup.");
        }

        /// <summary>
        /// Resets the initialization state and triggers re-initialization.
        /// Should be called after network recovery before needing settings again.
        /// </summary>
        /// <returns>The new initialization task's eventual result (true for success, false/exception for failure).</returns>
        public async Task<bool> ResetAndReinitializeAsync()
        {
            await _initSemaphore.WaitAsync();
            try
            {
                // Only reset if already attempted or completed
                if (_initializationLazyTask.IsValueCreated)
                {
                    _log.LogWarning("Resetting AzureHandler initialization state.");
                    _isInitializedSuccessfully = false;
                    _settings = null; // Clear potentially stale settings
                    // Create a new Lazy instance to allow re-execution
                    _initializationLazyTask = new Lazy<Task<bool>>(InitializeInternalAsync, LazyThreadSafetyMode.ExecutionAndPublication);
                    _log.LogInformation("AzureHandler state reset. Re-initialization will occur on next access to InitializationTask.");
                }
                else
                {
                    _log.LogInformation("AzureHandler not yet initialized. Reset is unnecessary.");
                }
            }
            finally
            {
                _initSemaphore.Release();
            }
            // Trigger and return the new task result by accessing .Value
            // The await handles the task execution and returns the bool result or propagates exception.
            return await InitializationTask;
        }

        /// <summary>
        /// Internal method containing the actual initialization logic. Fetches secrets and keys using ClientSecretCredential.
        /// Returns true on success, false on failure.
        /// </summary>
        private async Task<bool> InitializeInternalAsync()
        {
            await _initSemaphore.WaitAsync();
            try
            {
                // Prevent re-running if already successfully initialized
                if (_isInitializedSuccessfully)
                {
                    _log.LogInformation("AzureHandler already initialized successfully. Skipping.");
                    return true;
                }

                _log.LogInformation("Starting AzureHandler initialization attempt using ClientSecretCredential...");
                _settings = new AzureSettings();

                // --- Use ClientSecretCredential (As requested) ---
                if (string.IsNullOrEmpty(TenantId) || string.IsNullOrEmpty(ClientId) || string.IsNullOrEmpty(ClientSecret))
                {
                    throw new InvalidOperationException("TenantId, ClientId, or ClientSecret is missing for ClientSecretCredential.");
                }
                var credential = new ClientSecretCredential(TenantId, ClientId, ClientSecret);
                _log.LogInformation("Azure authentication credential (ClientSecretCredential) created.");
                // --- End Credential ---


                // --- Connect to Key Vault ---
                var keyVaultUri = new Uri(KeyVaultUriString);
                var secretClient = new SecretClient(keyVaultUri, credential);
                //_log.LogInformation("SecretClient created for Key Vault: {KeyVaultUri}", keyVaultUri);

                // --- Fetch Secrets/Settings Sequentially ---
                _settings.DatabaseConnectionString = await GetSecretAsync(secretClient, "EdgeTableStorage");
                _settings.SendMessageFunctionKey = await GetSecretAsync(secretClient, "SendMessageFunctionKey");
                _settings.UpdateUserFunctionKey = await GetSecretAsync(secretClient, "UpdateUserFunctionKey");
                _settings.DisconnectUserFunctionKey = await GetSecretAsync(secretClient, "DisconnectUserFunctionKey");
                _settings.RespondToAlertFunctionKey = await GetSecretAsync(secretClient, "RespondToAlertFunctionKey");
                _settings.DefaultHostKey = await GetSecretAsync(secretClient, "DefaultHostKey");

                // --- Validate Critical Settings ---
                if (string.IsNullOrEmpty(_settings.DatabaseConnectionString))
                {
                    throw new InvalidOperationException("Critical setting 'DatabaseConnectionString' could not be retrieved using ClientSecretCredential.");
                }
                if (string.IsNullOrEmpty(_settings.DefaultHostKey))
                {
                    _log.LogWarning("Setting 'DefaultHostKey' was not retrieved. Dependent functions may fail.");
                    // Decide if fatal: throw new InvalidOperationException("Critical setting 'DefaultHostKey' could not be retrieved.");
                }
                // Add other critical checks...

                // --- Mark as Success ---
                _isInitializedSuccessfully = true;
                _log.LogInformation("AzureHandler initialization completed successfully using ClientSecretCredential.");
                return true; // Signal success

            }
            catch (Exception ex)
            {
                // Log the critical failure
                _log.LogCritical(ex, "AzureHandler initialization failed critically using ClientSecretCredential.");
                _isInitializedSuccessfully = false; // Ensure flag is false
                _settings = null; // Clear potentially partial settings
                                  // Re-throw to fault the Lazy<Task> and allow MainViewModel to handle
                throw;
            }
            finally
            {
                _initSemaphore.Release();
            }
        }

        /// <summary>
        /// Helper method to fetch a secret from Key Vault with detailed logging and error handling.
        /// Returns null if the secret cannot be retrieved.
        /// </summary>
        private async Task<string?> GetSecretAsync(SecretClient client, string secretName)
        {
            if (string.IsNullOrEmpty(secretName))
            {
                _log.LogWarning("GetSecretAsync called with null or empty secret name.");
                return null;
            }

            try
            {
                //_log.LogInformation("Attempting to retrieve secret '{SecretName}' from Key Vault.", secretName);
                KeyVaultSecret secret = await client.GetSecretAsync(secretName).ConfigureAwait(false);
                //_log.LogInformation("Successfully retrieved secret '{SecretName}'.", secretName);
                return secret.Value;
            }
            catch (AuthenticationFailedException authEx) // Specific to the credential type
            {
                _log.LogError(authEx, "Authentication failed (ClientSecretCredential?) while retrieving secret '{SecretName}'. Check Service Principal details/secret validity.", secretName);
                return null;
            }
            catch (RequestFailedException rfEx) when (rfEx.Status == 404)
            {
                _log.LogError(rfEx, "Secret '{SecretName}' not found in Key Vault.", secretName);
                return null;
            }
            catch (RequestFailedException rfEx)
            {
                _log.LogError(rfEx, "Request failed while retrieving secret '{SecretName}'. Status: {Status}. Check Key Vault URI, network connectivity, and permissions.", secretName, rfEx.Status);
                return null;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "An unexpected error occurred while retrieving secret '{SecretName}'.", secretName);
                return null;
            }
        }

        // Method to get settings - remains the same conceptually
        public AzureSettings? GetAzureSettings()
        {
            if (!_isInitializedSuccessfully)
            {
                _log.LogWarning("GetAzureSettings called but AzureHandler is not successfully initialized.");
                return null;
            }
            return _settings;
        }
    }
}