using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;

namespace EdgeAlertFunc.AssetManagement
{
    public static class AssetFunctions
    {
        private const string TableName = "AssetManagement";

        [FunctionName("CreateAsset")]
        public static async Task<IActionResult> CreateAsset(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "assets")] HttpRequest req,
            ILogger log)
        {
            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            var service = new AssetTableService(connectionString, TableName);
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var asset = JsonSerializer.Deserialize<AssetEntity>(requestBody);
            await service.AddOrUpdateAssetAsync(asset);
            return new OkObjectResult(asset);
        }

        [FunctionName("GetAssets")]
        public static async Task<IActionResult> GetAssets(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "assets")] HttpRequest req,
            ILogger log)
        {
            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            var service = new AssetTableService(connectionString, TableName);
            bool includeDeleted = req.Query.ContainsKey("includeDeleted") && req.Query["includeDeleted"] == "true";
            var assets = await service.GetAssetsAsync(includeDeleted);
            return new OkObjectResult(assets);
        }

        [FunctionName("UpdateAsset")]
        public static async Task<IActionResult> UpdateAsset(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "assets/{assetId}")] HttpRequest req,
            string assetId,
            ILogger log)
        {
            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            var service = new AssetTableService(connectionString, TableName);
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var asset = JsonSerializer.Deserialize<AssetEntity>(requestBody);
            asset.AssetId = assetId;
            await service.AddOrUpdateAssetAsync(asset);
            return new OkObjectResult(asset);
        }

        [FunctionName("DeleteAsset")]
        public static async Task<IActionResult> DeleteAsset(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "assets/{assetId}/{odsLocation}")] HttpRequest req,
            string assetId,
            string odsLocation,
            ILogger log)
        {
            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            var service = new AssetTableService(connectionString, TableName);
            await service.SoftDeleteAssetAsync(assetId, odsLocation);
            return new OkResult();
        }

        [FunctionName("RecoverAsset")]
        public static async Task<IActionResult> RecoverAsset(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "assets/{assetId}/{odsLocation}/recover")] HttpRequest req,
            string assetId,
            string odsLocation,
            ILogger log)
        {
            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            var service = new AssetTableService(connectionString, TableName);
            await service.RecoverAssetAsync(assetId, odsLocation);
            return new OkResult();
        }
    }
}
