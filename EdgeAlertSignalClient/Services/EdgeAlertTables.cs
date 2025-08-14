using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Microsoft.VisualBasic.ApplicationServices;

namespace EdgeAlertSignalClient.Services
{
    public class EdgeUser
    {
        public string? UserName { get; set; }
        public string? HostName { get; set; }
        public string? SerialNumber { get; set; }
        public string? ODS { get; set; }
        public string? Room { get; set; }
        public string? Alert { get; set; }
        public string? LoggedIn { get; set; }
        public string? LastOnline { get; set; }
        public string? LastPing { get; set; }
        public bool? Connected { get; set; }
        public string Version { get; set; }
        public bool IsCurrentUser { get; set; }
        public bool HasCurrentUserResponded { get; set; }
    }

    public class EdgeUserViewModel : EdgeUser
    {
        public string? DateTimeLoggedIn { get; set; }
        public ObservableCollection<Responder>? Responders { get; set; }
    }

    public class Responder
    {
        public string? AlertTime { get; set; }
        public string? ResponderName { get; set; }
        public string? ResponderHostName { get; set; }
        public string? AlertName { get; set; }
        public string? ResponderCancelled { get; set; }
    }


    public class EdgeAlertTables
    {
        private readonly TableClient _tableClient;

        public EdgeAlertTables(string connectionString, string tableName)
        {
            _tableClient = new TableClient(connectionString, tableName);
        }

        public async Task CreateTableAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task AddOrUpdateUserAsync(EdgeUser user)
        {
            var entity = new TableEntity(user.UserName.ToUpper(), user.HostName)
            {
                ["ODS"] = user.ODS,
                ["Room"] = user.Room,
                ["Alert"] = user.Alert,
                ["LoggedIn"] = user.LoggedIn,
                ["LastOnline"] = user.LastOnline,
                ["Connected"] = user.Connected,
                ["Version"] = user.Version
            };

            await _tableClient.UpsertEntityAsync(entity);
        }

        public async Task<EdgeUser> GetUserAsync(string userName, string hostName)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<TableEntity>(userName.ToUpper(), hostName);

                if (response != null)
                {
                    var entity = response.Value;
                    return new EdgeUser
                    {
                        UserName = entity.PartitionKey,
                        HostName = entity.RowKey,
                        ODS = entity.GetString("ODS"),
                        Room = entity.GetString("Room"),
                        Alert = entity.GetString("Alert"),
                        LoggedIn = entity.GetString("LoggedIn"),
                        LastOnline = entity.GetString("LastOnline"),
                        Connected = entity.GetBoolean("Connected"),
                        Version = entity.GetString("Version")
                    };
                }
            }
            catch (Azure.RequestFailedException ex)
            {
                if (ex.Status == 404) // Resource not found
                {
                    // Handle the not found case, for example, return null or log the error
                    return null;
                }
                else
                {
                    // If it's another error, you might want to rethrow the exception or handle it differently
                    return null;
                }
            }

            return null;
        }

        public async Task<Dictionary<string, DateTime?>> GetAllUserTimestampsAsync(string odsFilter = null)
        {
            Dictionary<string, DateTime?> timestampDict = new Dictionary<string, DateTime?>();
            string filter = odsFilter != null ? $"ODS eq '{odsFilter}'" : null;

            try
            {
                await foreach (var entity in _tableClient.QueryAsync<TableEntity>(filter: filter))
                {
                    if (entity != null)
                    {
                        string uniqueKey = entity.PartitionKey.ToUpper() + "_" + entity.RowKey; // Combine UserName and HostName
                        DateTime? timestamp = null;

                        // Check if the Timestamp has a value
                        if (entity.Timestamp.HasValue)
                        {
                            timestamp = entity.Timestamp.Value.UtcDateTime;
                        }

                        timestampDict[uniqueKey] = timestamp;
                    }
                }
            }
            catch (Azure.RequestFailedException ex)
            {
                if (ex.Status == 404) // Resource not found
                {
                    return new Dictionary<string, DateTime?>();
                }
                else
                {
                    return new Dictionary<string, DateTime?>();
                }
            }

            return timestampDict;
        }

        public async Task<ObservableCollection<EdgeUser>> GetAllUserAsync()
        {
            ObservableCollection<EdgeUser> users = new ObservableCollection<EdgeUser>();

            try
            {
                await foreach (var entity in _tableClient.QueryAsync<TableEntity>())
                {
                    if (entity != null)
                    {
                        var user = new EdgeUser
                        {
                            UserName = entity.PartitionKey.ToUpper(),
                            HostName = entity.RowKey,
                            ODS = entity.GetString("ODS"),
                            Room = entity.GetString("Room"),
                            Alert = entity.GetString("Alert"),
                            LoggedIn = entity.GetString("LoggedIn"),
                            LastOnline = entity.GetString("LastOnline"),
                            Connected = entity.GetBoolean("Connected"),
                            Version = entity.GetString("Version")
                        };

                        users.Add(user);
                    }
                }
            }
            catch (Azure.RequestFailedException ex)
            {
                if (ex.Status == 404) // Resource not found
                {
                    // Handle the not found case, for example, return an empty ObservableCollection or log the error
                    return new ObservableCollection<EdgeUser>();
                }
                else
                {
                    // If it's another error, you might want to rethrow the exception or handle it differently
                    throw;
                }
            }

            return users;
        }


        public async Task<ObservableCollection<EdgeUser>> GetFilteredUsersAsync(string odsFilter = null)
        {
            ObservableCollection<EdgeUser> users = new ObservableCollection<EdgeUser>();
            string filter = odsFilter != null ? $"ODS eq '{odsFilter}'" : null;

            try
            {
                await foreach (var entity in _tableClient.QueryAsync<TableEntity>(filter: filter))
                {
                    if (entity != null)
                    {
                        var user = new EdgeUser
                        {
                            UserName = entity.PartitionKey.ToUpper(),
                            HostName = entity.RowKey,
                            ODS = entity.GetString("ODS"),
                            Room = entity.GetString("Room"),
                            Alert = entity.GetString("Alert"),
                            LoggedIn = entity.GetString("LoggedIn"),
                            LastOnline = entity.GetString("LastOnline"),
                            Connected = entity.GetBoolean("Connected"),
                            Version = entity.GetString("Version")
                        };

                        users.Add(user);
                    }
                }
            }
            catch (Azure.RequestFailedException ex)
            {
                if (ex.Status == 404) // Resource not found
                {
                    // Handle the not found case, for example, return an empty ObservableCollection or log the error
                    return new ObservableCollection<EdgeUser>();
                }
                else
                {
                    // If it's another error, you might want to rethrow the exception or handle it differently
                    throw;
                }
            }

            return users;
        }

        public async Task DeleteUserAsync(string userName, string hostName)
        {
            await _tableClient.DeleteEntityAsync(userName.ToUpper(), hostName);
        }

        public async Task<int> GetUserCountAsync(string odsFilter = null)
        {
            int count = 0;
            string filter = odsFilter != null ? $"ODS eq '{odsFilter}'" : null;
            await foreach (var entity in _tableClient.QueryAsync<TableEntity>(filter: filter))
            {
                count++;
            }

            return count;
        }
    }
}
