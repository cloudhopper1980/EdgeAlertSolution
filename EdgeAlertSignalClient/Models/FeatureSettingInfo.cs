using System;
using System.Text.Json.Serialization; // Required for JsonPropertyName

namespace EdgeAlertSignalClient.Models // Or EdgeAlertSignalClient.DTOs
{
    /// <summary>
    /// Data Transfer Object representing a feature setting retrieved from the backend.
    /// Used within the EdgeAlertSignalClient application.
    /// </summary>
    public class FeatureSettingInfo
    {
        // Explicitly map the JSON property "PartitionKey" to this C# property
        [JsonPropertyName("partitionKey")]
        public string FeatureName { get; set; }

        // Explicitly map the JSON property "RowKey" to this C# property
        [JsonPropertyName("rowKey")]
        public string ScopeIdentifier { get; set; }

        // Mapping happens automatically due to matching name (case-insensitive)
        // but adding the attribute is good practice for clarity.
        [JsonPropertyName("isEnabled")]
        public bool IsEnabled { get; set; }

        // We typically don't need Timestamp or ETag on the client for this purpose
    }

    /// <summary>
    /// Data Transfer Object representing the response from the GetEffectiveFeatureSetting endpoint.
    /// </summary>
    public class EffectiveFeatureSettingResult
    {
        // Matches the JSON property name expected from the backend function
        [JsonPropertyName("effectiveIsEnabled")]
        public bool EffectiveIsEnabled { get; set; }
    }
}