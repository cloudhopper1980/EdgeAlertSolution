using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EdgeAlertFunc.AssetManagement
{
    public class AssetTableService
    {
        private readonly TableClient _tableClient;
        public AssetTableService(string connectionString, string tableName)
        {
            _tableClient = new TableClient(connectionString, tableName);
            _tableClient.CreateIfNotExists();
        }

        public async Task<List<AssetEntity>> GetAssetsAsync(bool includeDeleted = false)
        {
            var query = includeDeleted ? _tableClient.QueryAsync<AssetEntity>() :
                _tableClient.QueryAsync<AssetEntity>(x => x.DeletedAt == null);
            var result = new List<AssetEntity>();
            await foreach (var entity in query)
            {
                result.Add(entity);
            }
            return result;
        }

        public async Task AddOrUpdateAssetAsync(AssetEntity entity)
        {
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
    }
}
