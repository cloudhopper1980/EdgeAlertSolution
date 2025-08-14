using Azure;
using Azure.Data.Tables;
using EdgeAlertSignalClient.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EdgeAlertSignalClient.Services
{
    public class AssetTableService
    {
        private readonly TableClient _tableClient;
        public AssetTableService(string connectionString, string tableName)
        {
            _tableClient = new TableClient(connectionString, tableName);
            _tableClient.CreateIfNotExists();
        }

        public async Task<List<AssetItem>> GetAssetsAsync(bool includeDeleted = false)
        {
            var query = includeDeleted ? _tableClient.QueryAsync<AssetEntity>() :
                _tableClient.QueryAsync<AssetEntity>(x => x.DeletedAt == null);
            var result = new List<AssetItem>();
            await foreach (var entity in query)
            {
                result.Add(entity.ToAssetItem());
            }
            return result;
        }

        public async Task AddOrUpdateAssetAsync(AssetItem item)
        {
            var entity = AssetEntity.FromAssetItem(item);
            entity.UpdatedAt = DateTime.UtcNow;
            if (string.IsNullOrEmpty(entity.AssetId))
            {
                entity.AssetId = Guid.NewGuid().ToString();
                entity.CreatedAt = DateTime.UtcNow;
            }
            await _tableClient.UpsertEntityAsync(entity);
        }

        public async Task SoftDeleteAssetAsync(string assetId, string odsLocation)
        {
            var entity = await _tableClient.GetEntityAsync<AssetEntity>(odsLocation, assetId);
            entity.Value.DeletedAt = DateTime.UtcNow;
            await _tableClient.UpdateEntityAsync(entity.Value, entity.Value.ETag);
        }

        public async Task RecoverAssetAsync(string assetId, string odsLocation)
        {
            var entity = await _tableClient.GetEntityAsync<AssetEntity>(odsLocation, assetId);
            entity.Value.DeletedAt = null;
            await _tableClient.UpdateEntityAsync(entity.Value, entity.Value.ETag);
        }

        // ...other methods for filtering, searching, etc.

        // Table Entity for Azure Table Storage
        public class AssetEntity : ITableEntity
        {
            public string PartitionKey { get => OdsLocation ?? string.Empty; set => OdsLocation = value; }
            public string RowKey { get => AssetId ?? string.Empty; set => AssetId = value; }
            public DateTimeOffset? Timestamp { get; set; }
            public ETag ETag { get; set; }

            public string? AssetId { get; set; }
            public string? AssetType { get; set; }
            public string? Manufacturer { get; set; }
            public string? Model { get; set; }
            public string? SerialNumber { get; set; }
            public string? AssignedTo { get; set; }
            public string? OdsLocation { get; set; }
            public string? Status { get; set; }
            public string? Notes { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime UpdatedAt { get; set; }
            public DateTime? DeletedAt { get; set; }
            public DateTime? PurchaseDate { get; set; }
            public DateTime? WarrantyExpiryDate { get; set; }

            public static AssetEntity FromAssetItem(AssetItem item)
            {
                return new AssetEntity
                {
                    AssetId = item.AssetId ?? string.Empty,
                    AssetType = item.AssetType ?? string.Empty,
                    Manufacturer = item.Manufacturer ?? string.Empty,
                    Model = item.Model ?? string.Empty,
                    SerialNumber = item.SerialNumber ?? string.Empty,
                    PurchaseDate = item.PurchaseDate,
                    WarrantyExpiryDate = item.WarrantyExpiryDate,
                    AssignedTo = item.AssignedTo ?? string.Empty,
                    OdsLocation = item.OdsLocation ?? string.Empty,
                    Status = item.Status ?? string.Empty,
                    Notes = item.Notes ?? string.Empty,
                    CreatedAt = item.CreatedAt,
                    UpdatedAt = item.UpdatedAt,
                    DeletedAt = item.DeletedAt
                };
            }
            public AssetItem ToAssetItem()
            {
                return new AssetItem
                {
                    AssetId = this.AssetId ?? string.Empty,
                    AssetType = this.AssetType ?? string.Empty,
                    Manufacturer = this.Manufacturer ?? string.Empty,
                    Model = this.Model ?? string.Empty,
                    SerialNumber = this.SerialNumber ?? string.Empty,
                    PurchaseDate = this.PurchaseDate,
                    WarrantyExpiryDate = this.WarrantyExpiryDate,
                    AssignedTo = this.AssignedTo ?? string.Empty,
                    OdsLocation = this.OdsLocation ?? string.Empty,
                    Status = this.Status ?? string.Empty,
                    Notes = this.Notes ?? string.Empty,
                    CreatedAt = this.CreatedAt,
                    UpdatedAt = this.UpdatedAt,
                    DeletedAt = this.DeletedAt
                };
            }
        }
    }
}
