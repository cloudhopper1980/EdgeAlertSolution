// In EdgeAlertSignalClient\Models\ReportPermission.cs
using System.Text.Json.Serialization;

namespace EdgeAlertSignalClient.Models // Adjust namespace if needed
{
    public class ReportPermission
    {
        [JsonPropertyName("accessorUserId")]
        public string AccessorUserId { get; set; }

        [JsonPropertyName("targetOds")]
        public string TargetOds { get; set; } 

        [JsonPropertyName("permissionLevel")]
        public string PermissionLevel { get; set; }

        [JsonPropertyName("remove")]
        public bool Remove { get; set; } = false;

        /// <summary>
        /// Helper property for displaying the Practice Name in the UI.
        /// This is populated by the ViewModel based on TargetOds.
        /// JsonIgnore prevents sending this back to the server.
        /// </summary>
        [JsonIgnore]
        public string TargetPracticeName { get; set; }
    }
}