using Azure;
using Azure.Data.Tables;
using System;

namespace EdgeAlertFunc.AssetManagement
{
    public class AssetEntity : ITableEntity
    {
        public string PartitionKey { get => OdsLocation ?? string.Empty; set => OdsLocation = value; }
        public string RowKey { get => AssetId ?? string.Empty; set => AssetId = value; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string AssetId { get; set; }
        public string AssetType { get; set; }
        public string Manufacturer { get; set; }
        public string Model { get; set; }
        public string SerialNumber { get; set; }
        public string AssignedTo { get; set; }
        public string OdsLocation { get; set; }
        public string Status { get; set; }
        public string Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
        public DateTime? PurchaseDate { get; set; }
        public DateTime? WarrantyExpiryDate { get; set; }
    }
}
