using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Linq;

namespace EdgeAlertFunc.AssetManagement
{
    public static class AssetCsvFunctions
    {
        private const string TableName = "AssetManagement";

        [FunctionName("ImportAssetsCsv")]
        public static async Task<IActionResult> ImportAssetsCsv(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "assets/import")] HttpRequest req,
            ILogger log)
        {
            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            var service = new AssetTableService(connectionString, TableName);

            if (!req.HasFormContentType || !req.Form.Files.Any())
                return new BadRequestObjectResult("No file uploaded.");

            var file = req.Form.Files[0];
            var assets = new List<AssetEntity>();
            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                string headerLine = await reader.ReadLineAsync();
                var headers = headerLine.Split(',');
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    var values = line.Split(',');
                    var asset = new AssetEntity
                    {
                        AssetType = values[Array.IndexOf(headers, "AssetType")],
                        Manufacturer = values[Array.IndexOf(headers, "Manufacturer")],
                        Model = values[Array.IndexOf(headers, "Model")],
                        SerialNumber = values[Array.IndexOf(headers, "SerialNumber")],
                        PurchaseDate = DateTime.TryParse(values[Array.IndexOf(headers, "PurchaseDate")], out var pd) ? pd : (DateTime?)null,
                        WarrantyExpiryDate = DateTime.TryParse(values[Array.IndexOf(headers, "WarrantyExpiryDate")], out var wd) ? wd : (DateTime?)null,
                        AssignedTo = values[Array.IndexOf(headers, "AssignedTo")],
                        OdsLocation = values[Array.IndexOf(headers, "ODSLocation")],
                        Status = values[Array.IndexOf(headers, "Status")],
                        Notes = values[Array.IndexOf(headers, "Notes")],
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    assets.Add(asset);
                }
            }
            foreach (var asset in assets)
            {
                await service.AddOrUpdateAssetAsync(asset);
            }
            return new OkObjectResult($"Imported {assets.Count} assets.");
        }

        [FunctionName("DownloadAssetCsvTemplate")]
        public static IActionResult DownloadAssetCsvTemplate(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "assets/template")] HttpRequest req,
            ILogger log)
        {
            var csv = new StringBuilder();
            csv.AppendLine("AssetType,Manufacturer,Model,SerialNumber,PurchaseDate,WarrantyExpiryDate,AssignedTo,ODSLocation,Status,Notes");
            var bytes = Encoding.UTF8.GetBytes(csv.ToString());
            return new FileContentResult(bytes, "text/csv") { FileDownloadName = "AssetTemplate.csv" };
        }
    }
}
