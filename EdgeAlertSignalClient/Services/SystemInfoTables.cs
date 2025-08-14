using Azure;
using Azure.Data.Tables;
using EdgeAlertSignalClient.Handlers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace EdgeAlertSignalClient.Services
{
    public class SystemInfoTables
    {
        private readonly TableClient _newTableClient;
        private readonly TableClient _oldTableClient;
        private readonly JSONHandler _jsonHandler;
        private readonly ILogger _log;
        // Cache for the SerialNumber to avoid redundant lookups or generations.
        private string _cachedSerialNumber = null;

        public SystemInfoTables(string connectionString, string oldTableName, string newTableName, JSONHandler jsonHandler, ILogger log)
        {
            _oldTableClient = new TableClient(connectionString, oldTableName); // Old table
            _newTableClient = new TableClient(connectionString, newTableName); // New table
            _jsonHandler = jsonHandler;
            _log = log;
        }

        public async Task CreateTableAsync()
        {
            await _newTableClient.CreateIfNotExistsAsync();
        }

        public async Task AddOrUpdateSystemInfoAsync(SystemInfoService systemInfo)
        {
            // Ensure SerialNumber is valid; fallback to cached value if available
            if (string.IsNullOrEmpty(systemInfo.SerialNumber))
            {
                throw new ArgumentException("SystemInfo must have a valid SerialNumber.");
            }

            string serialNumber = systemInfo.SerialNumber.ToUpper();

            var entity = new TableEntity(serialNumber, serialNumber)
            {
                ["HostName"] = systemInfo.HostName,
                ["SerialNumber"] = serialNumber,
                ["IpAddress"] = systemInfo.IpAddress,
                ["OperatingSystem"] = systemInfo.OperatingSystem,
                ["HardDriveTotalSpace"] = systemInfo.HardDriveTotalSpace,
                ["HardDriveFreeSpace"] = systemInfo.HardDriveFreeSpace,
                ["RamCapacity"] = systemInfo.RamCapacity,
                ["Manufacturer"] = systemInfo.Manufacturer,
                ["Model"] = systemInfo.Model,
                ["ODS"] = systemInfo.ODS,
                ["Room"] = systemInfo.Room,
                ["UserName"] = systemInfo.UserName,
                ["LastOnline"] = systemInfo.LastOnline,
                ["LoggedIn"] = systemInfo.LoggedIn,
                ["Version"] = systemInfo.Version
            };

            await _newTableClient.UpsertEntityAsync(entity);
        }

        private async Task<string> GenerateNextUniqueSerialNumberAsync()
        {
            int highestNumber = 0;

            // Query Azure Table Storage for the highest Unknown-Serial
            var query = _newTableClient.QueryAsync<TableEntity>(filter: $"PartitionKey ge 'Unknown-Serial-000001'");

            await foreach (var entity in query)
            {
                var partitionKey = entity.PartitionKey;
                if (partitionKey.StartsWith("Unknown-Serial-"))
                {
                    var numberPart = partitionKey.Substring("Unknown-Serial-".Length);
                    if (int.TryParse(numberPart, out int number))
                    {
                        highestNumber = Math.Max(highestNumber, number);
                    }
                }
            }

            // Increment the highest found number to create the next unique SerialNumber
            return $"Unknown-Serial-{(highestNumber + 1):D6}";
        }

        public async Task<ObservableCollection<SystemInfoService>> GetAllSystemInfoBySimilarODSAsync(string TrueODS)
        {
            var similarODSList = _jsonHandler.GetSimilarODS(TrueODS);
            var allSystemInfo = new List<SystemInfoService>();

            foreach (var similarODS in similarODSList)
            {
                var systemInfoForThisODS = await GetFilteredSystemInfoAsync(similarODS);
                allSystemInfo.AddRange(systemInfoForThisODS);
            }

            var mappedSystemInfo = allSystemInfo.Select(info =>
            {
                var transformedODS = _jsonHandler.GetLocationByODS(info.ODS)?.Practice;

                if (transformedODS != null)
                {
                    info.ODS = transformedODS;
                }

                return info;
            }).ToList();

            return new ObservableCollection<SystemInfoService>(mappedSystemInfo);
        }

        public async Task<ObservableCollection<SystemInfoService>> GetFilteredSystemInfoAsync(string odsFilter = null)
        {
            ObservableCollection<SystemInfoService> systemInfoList = new ObservableCollection<SystemInfoService>();
            string filter = odsFilter != null ? $"ODS eq '{odsFilter}'" : null;

            try
            {
                await foreach (var entity in _newTableClient.QueryAsync<TableEntity>(filter: filter))
                {
                    if (entity != null)
                    {
                        var systemInfo = new SystemInfoService
                        {
                            HostName = entity.GetString("HostName"), // Map HostName property
                            SerialNumber = entity.GetString("SerialNumber"),
                            IpAddress = entity.GetString("IpAddress"),
                            OperatingSystem = entity.GetString("OperatingSystem"),
                            HardDriveTotalSpace = entity.GetString("HardDriveTotalSpace"),
                            HardDriveFreeSpace = entity.GetString("HardDriveFreeSpace"),
                            RamCapacity = entity.GetString("RamCapacity"),
                            Manufacturer = entity.GetString("Manufacturer"),
                            Model = entity.GetString("Model"),
                            ODS = entity.GetString("ODS"),
                            Room = entity.GetString("Room"),
                            UserName = entity.GetString("UserName"),
                            LoggedIn = entity.GetString("LoggedIn"),
                            LastOnline = entity.GetString("LastOnline"),
                            Version = entity.GetString("Version")
                        };

                        systemInfoList.Add(systemInfo);
                    }
                }
            }
            catch (Azure.RequestFailedException ex)
            {
                if (ex.Status == 404)
                {
                    return new ObservableCollection<SystemInfoService>();
                }
                else
                {
                    throw;
                }
            }

            return systemInfoList;
        }

        public async Task MigrateToNewTableAsync()
        {
            await _newTableClient.CreateIfNotExistsAsync();

            var entities = new List<TableEntity>();
            await foreach (var entity in _oldTableClient.QueryAsync<TableEntity>())
            {
                entities.Add(entity);
            }

            _log.LogInformation($"Total rows fetched from old table: {entities.Count}");

            var groupedEntities = entities.GroupBy(entity => entity.GetString("SerialNumber") ?? entity.PartitionKey);

            _log.LogInformation($"Unique identifiers (SerialNumbers or PartitionKeys) found: {groupedEntities.Count()}");

            foreach (var group in groupedEntities)
            {
                var mostRecentEntity = group.OrderByDescending(entity =>
                {
                    var lastOnline = entity.GetString("LastOnline");
                    if (lastOnline == "Online")
                    {
                        return DateTime.MaxValue;
                    }
                    if (DateTime.TryParse(lastOnline, out var parsedDate))
                    {
                        return parsedDate;
                    }
                    return DateTime.MinValue;
                }).FirstOrDefault();

                if (mostRecentEntity != null)
                {
                    // Extract SerialNumber or generate a unique one if null
                    var serialNumber = mostRecentEntity.GetString("SerialNumber");
                    if (string.IsNullOrEmpty(serialNumber))
                    {
                        serialNumber = await GenerateNextUniqueSerialNumberAsync();
                        _log.LogInformation($"Generated unique serial for PartitionKey '{mostRecentEntity.PartitionKey}': {serialNumber}");
                    }

                    _log.LogInformation($"Migrating SerialNumber: {serialNumber}, LastOnline: {mostRecentEntity.GetString("LastOnline")}");

                    var newEntity = new TableEntity(serialNumber, serialNumber)
                    {
                        ["HostName"] = mostRecentEntity.PartitionKey, // Extract HostName from PartitionKey
                        ["SerialNumber"] = serialNumber,
                        ["IpAddress"] = mostRecentEntity.GetString("IpAddress"),
                        ["OperatingSystem"] = mostRecentEntity.GetString("OperatingSystem"),
                        ["HardDriveTotalSpace"] = mostRecentEntity.GetString("HardDriveTotalSpace"),
                        ["HardDriveFreeSpace"] = mostRecentEntity.GetString("HardDriveFreeSpace"),
                        ["RamCapacity"] = mostRecentEntity.GetString("RamCapacity"),
                        ["Manufacturer"] = mostRecentEntity.GetString("Manufacturer"),
                        ["Model"] = mostRecentEntity.GetString("Model"),
                        ["ODS"] = mostRecentEntity.GetString("ODS"),
                        ["Room"] = mostRecentEntity.GetString("Room"),
                        ["UserName"] = mostRecentEntity.GetString("UserName"),
                        ["LastOnline"] = mostRecentEntity.GetString("LastOnline"),
                        ["LoggedIn"] = mostRecentEntity.GetString("LoggedIn"),
                        ["Version"] = mostRecentEntity.GetString("Version")
                    };

                    await _newTableClient.UpsertEntityAsync(newEntity);
                }
                else
                {
                    _log.LogWarning($"No valid entity found for group with key: {group.Key}");
                }
            }
        }

        public async Task SyncToNewTableAsync()
        {
            await _newTableClient.CreateIfNotExistsAsync();

            // Current UTC time
            var now = DateTime.UtcNow;

            // Determine the start time for filtering
            DateTime filterTime;
            if (now.Hour <= 8) // If on or before 8 AM UTC
            {
                filterTime = now.Date.AddDays(-1); // Start from yesterday
            }
            else
            {
                filterTime = now.AddHours(-1); // Start from the last hour
            }

            string filter = $"LastOnline eq 'Online' or LastOnline ge datetime'{filterTime:yyyy-MM-ddTHH:mm:ss.fffZ}'";

            // Query the old table with the filter
            var entities = new List<TableEntity>();
            await foreach (var entity in _oldTableClient.QueryAsync<TableEntity>(filter))
            {
                entities.Add(entity);
            }

            _log.LogInformation($"Total rows fetched from old table after filtering: {entities.Count}");

            // Group by SerialNumber
            var groupedEntities = entities
                .Where(entity => !string.IsNullOrEmpty(entity.GetString("SerialNumber"))) // Ignore rows without SerialNumber
                .GroupBy(entity => entity.GetString("SerialNumber"));

            foreach (var group in groupedEntities)
            {
                var mostRecentEntity = group.OrderByDescending(entity =>
                {
                    var lastOnline = entity.GetString("LastOnline");
                    if (lastOnline == "Online")
                    {
                        return DateTime.MaxValue; // Treat "Online" as the most recent state
                    }
                    if (DateTime.TryParse(lastOnline, out var parsedDate))
                    {
                        return parsedDate; // Parse LastOnline as a DateTime
                    }
                    return DateTime.MinValue; // Treat invalid or empty dates as the oldest
                }).FirstOrDefault();

                if (mostRecentEntity != null)
                {
                    var serialNumber = mostRecentEntity.GetString("SerialNumber");

                    try
                    {
                        // Check if the entity exists in the new table
                        var existingEntityResponse = await _newTableClient.GetEntityIfExistsAsync<TableEntity>(serialNumber, serialNumber);

                        if (!existingEntityResponse.HasValue ||
                            CompareLastOnline(existingEntityResponse.Value.GetString("LastOnline"), mostRecentEntity.GetString("LastOnline")) < 0)
                        {
                            // Prepare the new entity
                            var newEntity = new TableEntity(serialNumber, serialNumber)
                            {
                                ["HostName"] = mostRecentEntity.PartitionKey, // Extract HostName from PartitionKey
                                ["SerialNumber"] = serialNumber,
                                ["IpAddress"] = mostRecentEntity.GetString("IpAddress"),
                                ["OperatingSystem"] = mostRecentEntity.GetString("OperatingSystem"),
                                ["HardDriveTotalSpace"] = mostRecentEntity.GetString("HardDriveTotalSpace"),
                                ["HardDriveFreeSpace"] = mostRecentEntity.GetString("HardDriveFreeSpace"),
                                ["RamCapacity"] = mostRecentEntity.GetString("RamCapacity"),
                                ["Manufacturer"] = mostRecentEntity.GetString("Manufacturer"),
                                ["Model"] = mostRecentEntity.GetString("Model"),
                                ["ODS"] = mostRecentEntity.GetString("ODS"),
                                ["Room"] = mostRecentEntity.GetString("Room"),
                                ["UserName"] = mostRecentEntity.GetString("UserName"),
                                ["LastOnline"] = mostRecentEntity.GetString("LastOnline"),
                                ["LoggedIn"] = mostRecentEntity.GetString("LoggedIn"),
                                ["Version"] = mostRecentEntity.GetString("Version")
                            };

                            // Upsert to the new table
                            await _newTableClient.UpsertEntityAsync(newEntity);
                        }
                    }
                    catch (RequestFailedException ex)
                    {
                        _log.LogError($"Failed to retrieve or upsert entity with SerialNumber {serialNumber}: {ex.Message}");
                    }
                }
                else
                {
                    _log.LogWarning($"No valid entity found for group with SerialNumber: {group.Key}");
                }
            }
        }

        // Helper method to compare LastOnline timestamps
        private int CompareLastOnline(string lastOnline1, string lastOnline2)
        {
            if (lastOnline1 == "Online")
            {
                return lastOnline2 == "Online" ? 0 : 1;
            }
            if (lastOnline2 == "Online")
            {
                return -1;
            }
            if (DateTime.TryParse(lastOnline1, out var date1) && DateTime.TryParse(lastOnline2, out var date2))
            {
                return date1.CompareTo(date2);
            }
            return 0; // Treat invalid or null dates as equal
        }
    }
}
