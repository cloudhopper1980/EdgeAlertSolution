using System;
using System.Text.Json.Serialization;

namespace EdgeAlertSignalClient.Models
{
    /// <summary>
    /// Represents aggregated daily time tracking data for a user.
    /// Designed to match data returned from the GetSummarizedReportData Azure Function.
    /// (Mirrors UserTimeSummaryEntity structure).
    /// </summary>
    public class SummarizedReportData
    {
        // PartitionKey from Azure Table (UserID/UserName)
        [JsonPropertyName("partitionKey")]
        public string PartitionKey { get; set; }

        // RowKey from Azure Table (Date_ODS)
        [JsonPropertyName("rowKey")]
        public string RowKey { get; set; }

        // UserID (e.g., Username) - often same as PartitionKey in this context
        [JsonPropertyName("userID")]
        public string UserID { get; set; }

        // ODS Practice Code
        [JsonPropertyName("ods")]
        public string ODS { get; set; }

        // The specific date this summary applies to
        [JsonPropertyName("date")]
        public DateTime Date { get; set; }

        // Total seconds spent in the Active state
        [JsonPropertyName("totalActiveSeconds")]
        public double TotalActiveSeconds { get; set; }

        // Total seconds spent in the Away state
        [JsonPropertyName("totalAwaySeconds")]
        public double TotalAwaySeconds { get; set; }

        // Total seconds spent in the Offline state
        [JsonPropertyName("totalOfflineSeconds")]
        public double TotalOfflineSeconds { get; set; }

        // Timestamp from Azure Table (optional for client)
        [JsonPropertyName("timestamp")]
        public DateTimeOffset? Timestamp { get; set; }
    }
}