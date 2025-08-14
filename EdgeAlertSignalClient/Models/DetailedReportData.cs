using System;
using System.Text.Json.Serialization;

namespace EdgeAlertSignalClient.Models
{
    /// <summary>
    /// Represents a single detailed state transition log entry.
    /// Designed to match data returned from the GetDetailedReportData Azure Function.
    /// (Mirrors UserTimeLogEntity structure, potentially simplified).
    /// </summary>
    public class DetailedReportData
    {
        // PartitionKey from Azure Table (UserName)
        [JsonPropertyName("partitionKey")]
        public string PartitionKey { get; set; }

        // RowKey from Azure Table (ReverseTicks_MachineID)
        [JsonPropertyName("rowKey")]
        public string RowKey { get; set; }

        // Explicit UserName field
        [JsonPropertyName("userName")]
        public string UserName { get; set; }

        // Explicit MachineID field
        [JsonPropertyName("machineID")]
        public string MachineID { get; set; }

        // ODS Practice Code
        [JsonPropertyName("ods")]
        public string ODS { get; set; }

        // State (Active, Away, Offline) - Keeping as string for flexibility from function
        [JsonPropertyName("state")]
        public string State { get; set; }

        // The start time of this specific state
        [JsonPropertyName("stateStartTime")]
        public DateTime StateStartTime { get; set; }

        // Duration of the *previous* state before this one started
        [JsonPropertyName("durationSeconds")]
        public double? DurationSeconds { get; set; }

        // Optional context for the state transition
        [JsonPropertyName("context")]
        public string Context { get; set; }

        // Timestamp from Azure Table (optional for client)
        [JsonPropertyName("timestamp")]
        public DateTimeOffset? Timestamp { get; set; }

        /// <summary>
        /// Calculated duration (in seconds) of the state listed in THIS row.
        /// Populated client-side after data retrieval.
        /// </summary>
        [JsonIgnore] // Prevent serialization issues if model is ever sent elsewhere
        public double? CalculatedDurationSeconds { get; set; }
    }
}