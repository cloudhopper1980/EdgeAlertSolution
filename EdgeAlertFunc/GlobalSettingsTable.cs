using System;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging; // Added for logging

namespace EdgeAlertFunc
{
    // Entity for storing global settings
    public class GlobalSettingEntity : ITableEntity
    {
        // PartitionKey: Category of setting (e.g., "TimeTracking")
        public string PartitionKey { get; set; }

        // RowKey: Name of the specific setting (e.g., "AwayTimeoutMinutes")
        public string RowKey { get; set; }

        // Required by ITableEntity
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        // Custom Property
        public string Value { get; set; } // The actual setting value stored as a string
    }

    // Class to handle interactions with the GlobalSettings table
    public class GlobalSettingsTable
    {
        private readonly TableClient _tableClient;
        private const string TableName = "GlobalSettings";
        private readonly ILogger _log; // Added logger

        public GlobalSettingsTable(string connectionString, ILogger log) // Pass ILogger
        {
            _tableClient = new TableClient(connectionString, TableName);
            _log = log;
        }

        // Ensure table exists
        public async Task CreateTableAsync()
        {
            try
            {
                await _tableClient.CreateIfNotExistsAsync();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Error creating table '{TableName}'.");
                throw; // Rethrow to indicate failure
            }
        }

        // Get a specific setting value
        public async Task<string> GetSettingAsync(string partitionKey, string settingName)
        {
            try
            {
                var response = await _tableClient.GetEntityIfExistsAsync<GlobalSettingEntity>(partitionKey, settingName);
                if (response.HasValue)
                {
                    return response.Value.Value;
                }
                _log.LogWarning($"Setting '{settingName}' with PartitionKey '{partitionKey}' not found.");
                return null; // Return null if setting doesn't exist
            }
            catch (RequestFailedException ex)
            {
                _log.LogError(ex, $"Error retrieving setting '{settingName}' with PartitionKey '{partitionKey}'. Status: {ex.Status}");
                return null; // Indicate error or non-existence
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Unexpected error retrieving setting '{settingName}' with PartitionKey '{partitionKey}'.");
                return null;
            }
        }

        // Add or Update a specific setting value
        public async Task<bool> UpdateSettingAsync(string partitionKey, string settingName, string settingValue)
        {
            try
            {
                var entity = new GlobalSettingEntity
                {
                    PartitionKey = partitionKey,
                    RowKey = settingName,
                    Value = settingValue,
                    Timestamp = DateTimeOffset.UtcNow // Track last update time
                };

                // Upsert the entity (updates if exists, inserts if not)
                await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
                _log.LogInformation($"Successfully updated setting '{settingName}' with PartitionKey '{partitionKey}'.");
                return true;
            }
            catch (RequestFailedException ex)
            {
                _log.LogError(ex, $"Error updating setting '{settingName}' with PartitionKey '{partitionKey}'. Status: {ex.Status}");
                return false;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Unexpected error updating setting '{settingName}' with PartitionKey '{partitionKey}'.");
                return false;
            }
        }
    }
}