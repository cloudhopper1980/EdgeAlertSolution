using System;

namespace EdgeAlertSignalClient.Models
{
    public class AssetItem
    {
        public string AssetId { get; set; } // GUID string
        public string AssetType { get; set; } // Dropdown
        public string Manufacturer { get; set; }
        public string Model { get; set; }
        public string SerialNumber { get; set; } // Required, unique
        public DateTime? PurchaseDate { get; set; }
        public DateTime? WarrantyExpiryDate { get; set; }
        public string AssignedTo { get; set; }
        public string OdsLocation { get; set; } // Dropdown, required
        public string Status { get; set; } // Active/Inactive/Archived
        public string Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; } // Soft delete
    }
}
