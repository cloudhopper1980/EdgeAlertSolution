using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.SignalRService;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.IO;
using Azure.Data.Tables;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using static EdgeAlertFunc.AlertTables;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using static EdgeAlertFunc.EdgeAlertTables;
using System.Security.Claims;
using System.Net.Http;
using System.Text.Json.Serialization;
using Azure;

namespace EdgeAlertFunc
{
    public static partial class EdgeAlertFunc
    {
        // Define Core Hours (assuming fixed UTC for simplicity)
        private static readonly TimeSpan CoreHoursStart = TimeSpan.FromHours(8); // 8 AM
        private static readonly TimeSpan CoreHoursEnd = TimeSpan.FromHours(18); // 6 PM (18:00)

        public class JsonLocation
        {
            [JsonPropertyName("ODS")]
            public string ODS { get; set; }

            [JsonPropertyName("practice")]
            public string Practice { get; set; }

            [JsonPropertyName("siteName")]
            public string SiteName { get; set; }

            [JsonPropertyName("ipAddressLow")]
            public string IpAddressLow { get; set; }

            [JsonPropertyName("ipAddressHigh")]
            public string IpAddressHigh { get; set; }

            [JsonPropertyName("domain")]
            public List<string> Domains { get; set; }

            [JsonPropertyName("rooms")]
            public ObservableCollection<string> Rooms { get; set; }
        }

        public class JsonRootObject
        {
            [JsonPropertyName("Locations")]
            public List<JsonLocation> Locations { get; set; } // Use List<T> for server-side
        }

        [FunctionName("negotiate")]
        public static async Task<SignalRConnectionInfo> Negotiate(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req,
            [SignalRConnectionInfo(HubName = "EdgeAlertHub")] SignalRConnectionInfo connectionInfo,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            return connectionInfo;
        }

        [FunctionName("negotiateV2")]
        public static async Task<SignalRConnectionInfo> NegotiateV2(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req,
            [SignalRConnectionInfo(HubName = "EdgeAlertHub", UserId = "{headers.x-ms-signalr-userid}")] SignalRConnectionInfo connectionInfo,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            return connectionInfo;
        }

        [FunctionName("sendmessage")]
        public static async Task<IActionResult> SendMessage(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
            [SignalR(HubName = "EdgeAlertHub")] IAsyncCollector<SignalRMessage> signalRMessages,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request to send a message.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            EdgeAlertTables.EdgeUser message = JsonSerializer.Deserialize<EdgeAlertTables.EdgeUser>(requestBody);

            await signalRMessages.AddAsync(new SignalRMessage
            {
                Target = "ReceiveEdgeAlertMessage",
                GroupName = message.ODS,
                Arguments = new object[] { message }
            });

            return new OkObjectResult("Message sent");
        }

        // JoinGroup Function
        [FunctionName("JoinGroup")]
        public static async Task<IActionResult> JoinGroup(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
            [SignalR(HubName = "EdgeAlertHub")] IAsyncCollector<SignalRGroupAction> signalRGroupActions,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request to join a group.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            EdgeUser user = JsonSerializer.Deserialize<EdgeUser>(requestBody);

            string userId = $"{user.UserName.ToUpper()}-{user.HostName.ToUpper()}"; // Concatenate UserName and HostName

            await signalRGroupActions.AddAsync(
                new SignalRGroupAction
                {
                    UserId = userId,
                    GroupName = user.ODS,
                    Action = GroupAction.Add
                });

            return new OkObjectResult($"User {userId} added to group {user.ODS}");
        }

        // LeaveGroup Function
        [FunctionName("LeaveGroup")]
        public static async Task<IActionResult> LeaveGroup(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
            [SignalR(HubName = "EdgeAlertHub")] IAsyncCollector<SignalRGroupAction> signalRGroupActions,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request to leave a group.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            EdgeUser user = JsonSerializer.Deserialize<EdgeUser>(requestBody);

            string userId = $"{user.UserName.ToUpper()}-{user.HostName.ToUpper()}"; // Concatenate UserName and HostName

            await signalRGroupActions.AddAsync(
                new SignalRGroupAction
                {
                    UserId = userId,
                    GroupName = user.ODS,
                    Action = GroupAction.Remove
                });

            return new OkObjectResult($"User {userId} removed from group {user.ODS}");
        }

        [FunctionName("UpdateUser")]
        public static async Task<IActionResult> UpdateUser(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
            [SignalR(HubName = "EdgeAlertHub")] IAsyncCollector<SignalRMessage> signalRMessages,
            [SignalR(HubName = "EdgeAlertHub")] IAsyncCollector<SignalRGroupAction> signalRGroupActions,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request to update user data.");

            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            var edgeAlertTables = new EdgeAlertTables(connectionString, "Users");
            var responderTables = new ResponderTables(connectionString, "Responders");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            EdgeAlertTables.EdgeUser user = JsonSerializer.Deserialize<EdgeAlertTables.EdgeUser>(requestBody);

            // Check client version or assume legacy if not specified
            string clientVersion = req.Headers["Client-Version"].FirstOrDefault();
            bool isNewClient = clientVersion == "2.0";

            string blockMessage = string.Empty;

            // Check if the user's ODS is in the banned list
            var bannedODS = await BannedODSHelper.GetBannedODSAsync();
            if (bannedODS.Contains(user.ODS, StringComparer.OrdinalIgnoreCase))
            {
                log.LogWarning($"UpdateUser request ignored for banned ODS: {user.ODS}");

                // Custom message for banned ODS
                blockMessage = $"Your License has expired, please contact support@edgebits.co.uk to renew your license.";

                // Handle response for legacy clients
                if (!isNewClient)
                {
                    log.LogInformation("Sending legacy response for blocked user.");
                    return new OkObjectResult("User data updated");
                }

                // New clients receive detailed info
                log.LogInformation("Sending new client response for blocked user.");
                return new OkObjectResult(new UpdateUserResponse { UserName = user.UserName, IsBlocked = true, Message = blockMessage });
            }

            // Proceed with user update if not blocked
            EdgeAlertTables.EdgeUser existingUser = await edgeAlertTables.GetUserAsync(user.UserName.ToUpper(), user.HostName.ToUpper() ?? user.UserName.ToUpper());
            string previousODS = existingUser?.ODS;

            string lastOnlineStatus = user.LastOnline;
            if (string.IsNullOrEmpty(lastOnlineStatus))
            {
                lastOnlineStatus = "Online";
            }

            var edgeUser = new EdgeAlertTables.EdgeUser
            {
                UserName = user.UserName.ToUpper(),
                HostName = user.HostName.ToUpper() ?? user.UserName.ToUpper(),
                ODS = user.ODS,
                Room = user.Room,
                LoggedIn = user.LoggedIn,
                LastOnline = lastOnlineStatus,
                LastPing = user.LastPing,
                Alert = user.Alert,
                Connected = user.Connected,
                Version = user.Version ?? ""
            };

            await edgeAlertTables.AddOrUpdateUserAsync(edgeUser);

            var userId = req.Headers["x-ms-signalr-userid"].FirstOrDefault();
            if (!string.IsNullOrEmpty(userId))
            {
                if (previousODS != user.ODS)
                {
                    if (!string.IsNullOrEmpty(previousODS))
                    {
                        log.LogInformation($"Removing user {userId} from group {previousODS}.");
                        await signalRGroupActions.AddAsync(new SignalRGroupAction
                        {
                            UserId = userId,
                            GroupName = previousODS,
                            Action = GroupAction.Remove
                        });

                        log.LogInformation($"Sending UpdateConnectedUserGroups message to group {previousODS}.");
                        await signalRMessages.AddAsync(new SignalRMessage
                        {
                            Target = "UpdateConnectedUserGroups",
                            GroupName = previousODS,
                            Arguments = new object[] { }
                        });
                    }
                }

                log.LogInformation($"Adding user {userId} to group {user.ODS}.");
                await signalRGroupActions.AddAsync(new SignalRGroupAction
                {
                    UserId = userId,
                    GroupName = user.ODS,
                    Action = GroupAction.Add
                });

                log.LogInformation($"Added user {userId} to group {user.ODS}.");
            }

            var alertTables = new AlertTables(connectionString, "Alerts");

            if (string.IsNullOrEmpty(user.Alert))
            {
                var lastAlert = await alertTables.GetLastAlertAsync(user.UserName.ToUpper(), user.HostName.ToUpper() ?? user.UserName.ToUpper(), user.ODS, user.Room);
                if (lastAlert != null && !string.IsNullOrEmpty(lastAlert.AlertStart) && string.IsNullOrEmpty(lastAlert.AlertStop))
                {
                    var edgeAlert = new AlertTables.Alert
                    {
                        UserName = user.UserName.ToUpper(),
                        HostName = user.HostName.ToUpper() ?? user.UserName.ToUpper(),
                        ODS = user.ODS,
                        Room = user.Room,
                        AlertStart = lastAlert.AlertStart,
                        AlertStop = Utils.ConvertUtcToGmtWithDst(DateTime.UtcNow)
                    };
                    await alertTables.UpdateAlertStopAsync(edgeAlert);
                }
            }
            else
            {
                var newAlert = new Alert
                {
                    UserName = user.UserName.ToUpper(),
                    HostName = user.HostName.ToUpper() ?? user.UserName.ToUpper(),
                    ODS = user.ODS,
                    Room = user.Room,
                    AlertStart = user.Alert,
                    AlertStop = string.Empty
                };
                await alertTables.AddOrUpdateAlertAsync(newAlert);
            }

            var usersInGroup = await GetFilteredUsersAsync(user.ODS, connectionString);
            var augmentedUsersInGroup = await AugmentUsersWithResponders(usersInGroup, connectionString);

            if (!string.IsNullOrEmpty(userId))
            {
                log.LogInformation($"Sending UpdateConnectedUserGroups message to group {user.ODS} with user data.");
                var signalRMessage = new SignalRMessage
                {
                    Target = "UpdateConnectedUserGroups",
                    GroupName = user.ODS,
                    Arguments = isNewClient ? new object[] { augmentedUsersInGroup } : new object[] { null }
                };
                await signalRMessages.AddAsync(signalRMessage);
            }
            else
            {
                log.LogInformation("Sending UpdateConnectedUsers message to all connected users.");
                await signalRMessages.AddAsync(new SignalRMessage
                {
                    Target = "UpdateConnectedUsers",
                    Arguments = new object[] { null }
                });
            }

            if (clientVersion == "legacy")
            {
                log.LogInformation("Sending legacy response.");
                return new OkObjectResult("User data updated");
            }
            else
            {
                log.LogInformation("Sending new client response.");
                return new OkObjectResult(new UpdateUserResponse { UserName = user.UserName, IsBlocked = false });
            }
        }

        private static async Task<ObservableCollection<EdgeUserViewModel>> AugmentUsersWithResponders(
            ObservableCollection<EdgeUser> users, string connectionString)
        {
            var augmentedUsers = new ObservableCollection<EdgeUserViewModel>();
            var tableClient = new TableServiceClient(connectionString).GetTableClient("Responders");

            foreach (var user in users)
            {
                var userViewModel = new EdgeUserViewModel
                {
                    UserName = user.UserName,
                    HostName = user.HostName,
                    ODS = user.ODS,
                    Room = user.Room,
                    Alert = user.Alert,
                    LoggedIn = user.LoggedIn,
                    LastOnline = user.LastOnline,
                    LastPing = user.LastPing,
                    Connected = user.Connected,
                    Version = user.Version,
                    Responders = new ObservableCollection<Responder>()
                };

                if (!string.IsNullOrEmpty(user.Alert))
                {
                    var responders = await GetRespondersByAlertTimeAsync(Utils.ConvertDateFormat(user.Alert), tableClient);
                    foreach (var responder in responders)
                    {
                        userViewModel.Responders.Add(responder);
                    }
                }

                augmentedUsers.Add(userViewModel);
            }

            return augmentedUsers;
        }

        private static async Task<ObservableCollection<Responder>> GetRespondersByAlertTimeAsync(string alertTime, TableClient tableClient)
        {
            ObservableCollection<Responder> responders = new ObservableCollection<Responder>();

            try
            {
                await foreach (var entity in tableClient.QueryAsync<TableEntity>($"RowKey eq '{alertTime}'", select: new[] { "PartitionKey", "RowKey", "AlertName", "ResponderCancelled", "ResponderHostName" }))
                {
                    if (entity != null)
                    {
                        var responder = new Responder
                        {
                            ResponderName = entity.PartitionKey,
                            AlertTime = entity.RowKey,
                            AlertName = entity.GetString("AlertName"),
                            ResponderCancelled = entity.GetString("ResponderCancelled"),
                            ResponderHostName = entity.GetString("ResponderHostName")
                        };

                        responders.Add(responder);
                    }
                }
            }
            catch (Azure.RequestFailedException ex)
            {
                if (ex.Status == 404) // Resource not found
                {
                    return new ObservableCollection<Responder>();
                }
                else
                {
                    throw;
                }
            }

            return responders;
        }

        private static async Task<ObservableCollection<EdgeUser>> GetFilteredUsersAsync(string odsFilter, string connectionString)
        {
            ObservableCollection<EdgeUser> users = new ObservableCollection<EdgeUser>();
            var tableClient = new TableServiceClient(connectionString).GetTableClient("Users");
            string filter = odsFilter != null ? $"ODS eq '{odsFilter}'" : null;

            try
            {
                await foreach (var entity in tableClient.QueryAsync<TableEntity>(filter: filter))
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

        [FunctionName("UserDisconnected")]
        public static async Task<IActionResult> UserDisconnected(
                    [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
                    [SignalR(HubName = "EdgeAlertHub")] IAsyncCollector<SignalRMessage> signalRMessages,
                    [SignalR(HubName = "EdgeAlertHub")] IAsyncCollector<SignalRGroupAction> signalRGroupActions,
                    ILogger log) // Ensure ILogger is passed in
        {
            log.LogInformation("C# HTTP trigger function processed a request to set user as disconnected (V3 - Backward Compatible).");

            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            // Ensure connection string exists
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogCritical("Azure Storage Connection String is missing or empty in application settings.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // Table clients
            var edgeAlertTables = new EdgeAlertTables(connectionString, "Users");
            // Client for the OLD SystemInfo table (uses HostName key)
            var systemInfoTablesOld = new SystemInfoTables(connectionString, "SystemInfo");
            // Client for the NEW SystemInfo2 table (uses SerialNumber key)
            var systemInfoTablesNew = new SystemInfoTables(connectionString, "SystemInfo2");

            string requestBody;
            EdgeAlertTables.EdgeUser userPayload; // Uses the inner class with nullable SerialNumber

            // Deserialize request body
            try
            {
                requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                // Use case-insensitive deserialization
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                userPayload = JsonSerializer.Deserialize<EdgeAlertTables.EdgeUser>(requestBody, options);

                // Basic validation
                if (userPayload == null || string.IsNullOrWhiteSpace(userPayload.UserName) || string.IsNullOrWhiteSpace(userPayload.HostName))
                {
                    log.LogError("UserDisconnected: Invalid request body. Missing UserName or HostName.");
                    return new BadRequestObjectResult("Invalid request body. Missing UserName or HostName.");
                }
                log.LogInformation($"UserDisconnected: Received disconnect request for User: {userPayload.UserName}, Host: {userPayload.HostName}, Serial provided: {!string.IsNullOrWhiteSpace(userPayload.SerialNumber)}");
            }
            catch (JsonException jsonEx)
            {
                log.LogError(jsonEx, "UserDisconnected: Error deserializing request body JSON.");
                return new BadRequestObjectResult($"Invalid JSON format: {jsonEx.Message}");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "UserDisconnected: Error reading request body.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // --- Update Users Table (Common Logic) ---
            EdgeAlertTables.EdgeUser existingUser = await edgeAlertTables.GetUserAsync(userPayload.UserName.ToUpper(), userPayload.HostName.ToUpper() ?? userPayload.UserName.ToUpper());
            string userOds = existingUser?.ODS ?? userPayload.ODS; // Get ODS for SignalR grouping later

            var lastOnlineTimestamp = Utils.ConvertUtcToGmtWithDst(DateTime.UtcNow);

            // Update the Users table entity in memory
            var userEntityToUpdate = new EdgeAlertTables.EdgeUser
            {
                UserName = userPayload.UserName.ToUpperInvariant(),
                HostName = userPayload.HostName.ToUpperInvariant(),
                Connected = false,
                LastOnline = lastOnlineTimestamp,
                // Preserve other fields from existingUser if found, otherwise use payload/defaults
                ODS = userOds,
                Room = existingUser?.Room ?? userPayload.Room,
                Alert = existingUser?.Alert ?? userPayload.Alert,
                LoggedIn = existingUser?.LoggedIn ?? userPayload.LoggedIn,
                LastPing = existingUser?.LastPing ?? userPayload.LastPing, // Or update ping time here?
                Version = existingUser?.Version ?? userPayload.Version ?? ""
            };

            // Update the Users table in Azure
            try
            {
                await edgeAlertTables.AddOrUpdateUserAsync(userEntityToUpdate);
                log.LogInformation($"UserDisconnected: Updated 'Users' table for User: {userEntityToUpdate.UserName}, Host: {userEntityToUpdate.HostName} - Set Connected=false, LastOnline={lastOnlineTimestamp}");
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"UserDisconnected: Failed to update 'Users' table for User: {userEntityToUpdate.UserName}, Host: {userEntityToUpdate.HostName}.");
                // Consider if this is fatal - for now, continue to SystemInfo update attempt
            }

            // --- Conditional SystemInfo/SystemInfo2 Update ---
            try
            {
                // Create the base SystemInfoService object
                var systemInfoData = new SystemInfoTables.SystemInfoService // Use the inner class definition
                {
                    HostName = userPayload.HostName,
                    UserName = userPayload.UserName.ToUpperInvariant(),
                    LastOnline = lastOnlineTimestamp,
                    SerialNumber = userPayload.SerialNumber // This will be null for old clients
                };

                if (!string.IsNullOrWhiteSpace(systemInfoData.SerialNumber))
                {
                    // --- NEW CLIENT --- Update SystemInfo2 using SerialNumber key
                    log.LogInformation($"UserDisconnected: New client detected (SerialNumber provided: {systemInfoData.SerialNumber}). Updating SystemInfo2 table.");
                    await systemInfoTablesNew.AddOrUpdateSystemInfoAsync(systemInfoData); // This uses SerialNumber key logic
                }
                else
                {
                    // --- OLD CLIENT --- Update SystemInfo using HostName key
                    log.LogInformation($"UserDisconnected: Old client detected (SerialNumber missing). Updating SystemInfo table.");
                    await systemInfoTablesOld.AddOrUpdateSystemInfoAsync(systemInfoData); // This uses HostName key logic
                }
            }
            catch (Exception ex)
            {
                // Log the error but continue to SignalR updates
                log.LogError(ex, $"UserDisconnected: Failed during SystemInfo/SystemInfo2 update for User: {userPayload.UserName}, Host: {userPayload.HostName}, Serial: {userPayload.SerialNumber ?? "N/A"}.");
            }


            // --- SignalR Group Updates ---
            var userId = req.Headers["x-ms-signalr-userid"].FirstOrDefault();
            // Use the ODS code determined earlier
            if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(userOds))
            {
                var usersInGroup = await GetFilteredUsersAsync(userOds, connectionString);
                var augmentedUsersInGroup = await AugmentUsersWithResponders(usersInGroup, connectionString);

                log.LogInformation($"UserDisconnected: Removing SignalR user '{userId}' from group '{userOds}'.");
                await signalRGroupActions.AddAsync(new SignalRGroupAction
                {
                    UserId = userId,
                    GroupName = userOds,
                    Action = GroupAction.Remove
                });

                log.LogInformation($"UserDisconnected: Sending updated user list to group '{userOds}'.");
                await signalRMessages.AddAsync(new SignalRMessage
                {
                    Target = "UpdateConnectedUserGroups",
                    GroupName = userOds,
                    Arguments = new object[] { augmentedUsersInGroup }
                });
                log.LogInformation($"UserDisconnected: Sent UpdateConnectedUserGroups message to group {userOds}.");
            }
            else
            {
                log.LogWarning($"UserDisconnected: Skipping SignalR group removal/update. UserID: '{userId}', ODS: '{userOds}'. Sending global update instead.");
                await signalRMessages.AddAsync(new SignalRMessage
                {
                    Target = "UpdateConnectedUsers", // Legacy target
                    Arguments = new object[] { }
                });
                log.LogInformation("UserDisconnected: Sent legacy UpdateConnectedUsers message to all clients.");
            }

            return new OkObjectResult("User disconnected status updated (backward compatible).");
        }

#if !DEBUG
        [FunctionName("Ping")]
        public static async Task Ping([TimerTrigger("0 0 0 * * *")] TimerInfo myTimer, // runs every day at midnight
                                       [SignalR(HubName = "EdgeAlertHub")] IAsyncCollector<SignalRMessage> signalRMessages,
                                       ILogger log)
        {
            log.LogInformation("Ping function called.");

            await signalRMessages.AddAsync(new SignalRMessage
            {
                Target = "Ping",
                Arguments = new object[] { }
            });

            log.LogInformation($"Ping messages sent to all connected clients at: {Utils.ConvertUtcToGmtWithDst(DateTime.UtcNow)}.");

            // Wait for 10 seconds after sending all the messages
            await Task.Delay(TimeSpan.FromSeconds(20));

            // Now run the "CheckPingsAndDeleteUsers" function
            await CheckPingsAndDeleteUsers(signalRMessages, log);

            log.LogInformation("Checked all users Ping time and deleted users who didn't respond.");
        }
#endif

        public static async Task CheckPingsAndDeleteUsers(IAsyncCollector<SignalRMessage> signalRMessages, ILogger log)
        {
            log.LogInformation("CheckPingsAndDeleteUsers function called.");

            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            var edgeAlertTables = new EdgeAlertTables(connectionString, "Users");

            // Get all users
            var allUsers = await edgeAlertTables.GetAllUserAsync();

            // Get the expected last ping timestamp in GMT format
            DateTime expectedLastPingTimeGmt = Utils.ConvertUtcToGmtWithDstToDateTime(DateTime.UtcNow.AddSeconds(-25));

            log.LogInformation($"Expected Last Ping must be within 25 seconds of: {expectedLastPingTimeGmt}.");

            foreach (var user in allUsers)
            {
                // Try to parse the LastPing of the user to a DateTime
                if (DateTime.TryParseExact(user.LastPing, "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime userLastPingTimeGmt))
                {
                    // If user's LastPing is less than the expectedLastPingTimeGmt, delete the user
                    if (userLastPingTimeGmt < expectedLastPingTimeGmt)
                    {
                        log.LogInformation($"User: {user.UserName}. Last Ping was: {userLastPingTimeGmt}, expected Ping is: {expectedLastPingTimeGmt}. Deleting Entry.");
                        await edgeAlertTables.DeleteUserAsync(user.UserName.ToUpper(), user.HostName.ToUpper());
                    }
                }
            }
            // Update everyone still connected
            await signalRMessages.AddAsync(new SignalRMessage
            {
                Target = "UpdateConnectedUsers",
                Arguments = new object[] { }
            });
        }

        [FunctionName("RespondToAlert")]
        public static async Task<IActionResult> AddResponder(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
            [SignalR(HubName = "EdgeAlertHub")] IAsyncCollector<SignalRMessage> signalRMessages,
            [SignalR(HubName = "EdgeAlertHub")] IAsyncCollector<SignalRGroupAction> signalRGroupActions,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request to respond to an alert.");

            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            var responderTables = new ResponderTables(connectionString, "Responders");
            var edgeAlertTables = new EdgeAlertTables(connectionString, "Users");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            ResponderTables.Responder responder = JsonSerializer.Deserialize<ResponderTables.Responder>(requestBody);

            var newResponder = new ResponderTables.Responder
            {
                AlertTime = responder.AlertTime,
                AlertName = responder.AlertName,
                ResponderName = responder.ResponderName,
                ResponderHostName = responder.ResponderHostName
            };

            await responderTables.AddOrUpdateResponderAsync(newResponder);

            // Look up the ODS for the Group based on the responder details
            EdgeAlertTables.EdgeUser existingUser = await edgeAlertTables.GetUserAsync(responder.ResponderName.ToUpper(), responder.ResponderHostName.ToUpper() ?? responder.ResponderName.ToUpper());

            if (existingUser == null)
            {
                return new NotFoundObjectResult($"User with username {responder.ResponderName.ToUpper()} and hostname {responder.ResponderHostName.ToUpper() ?? responder.ResponderName.ToUpper()} not found");
            }

            // Fetch all users in the same ODS group and augment them with responders
            var usersInGroup = await GetFilteredUsersAsync(existingUser.ODS, connectionString);
            var augmentedUsersInGroup = await AugmentUsersWithResponders(usersInGroup, connectionString);

            // Check if UserId is present
            var userId = req.Headers["x-ms-signalr-userid"].FirstOrDefault();
            // Check if the custom header is present and has the expected value
            var useGroupUpdateHeader = req.Headers["Use-Group-Update"].FirstOrDefault();
            const string expectedHeaderValue = "UseGroupUpdate"; // The value you expect

            string target = string.IsNullOrEmpty(useGroupUpdateHeader) || useGroupUpdateHeader != expectedHeaderValue
                ? "UpdateConnectedUsers" // Fallback to the original target if header is not present or value is not as expected
                : "UpdateConnectedUserGroups"; // Use new target if header is present and value matches

            if (target == "UpdateConnectedUserGroups")
            {
                // UserId is present, client is using new version
                await signalRMessages.AddAsync(new SignalRMessage
                {
                    Target = target,
                    GroupName = existingUser.ODS,
                    Arguments = new object[] { augmentedUsersInGroup }
                });
                // Logging after sending the message
                log.LogInformation($"Sent UpdateConnectedUserGroups message to group {existingUser.ODS} with updated user data.");
            }
            else
            {
                // UserId is not present, client is using old version
                await signalRMessages.AddAsync(new SignalRMessage
                {
                    Target = target,
                    GroupName = existingUser.ODS,
                    Arguments = new object[] { }
                });
                // Logging after sending the message
                log.LogInformation("Sent UpdateConnectedUsers message to all connected users.");
            }

            return new OkObjectResult("Responder added and user data updated");
        }

        [FunctionName("RefreshBannedODS")]
        public static async Task Run([TimerTrigger("0 */10 6-20 * * *")] TimerInfo myTimer, ILogger log)
        {
            log.LogInformation($"RefreshBannedODS function executed at: {DateTime.UtcNow}");

            await EdgeAlertFunc.BannedODSHelper.RefreshBannedODSCache();

            log.LogInformation("Banned ODS cache refreshed.");
        }

        public static class BannedODSHelper
        {
            private static readonly string BanUrl = "https://edgebitslimiteduksouth.blob.core.windows.net/edgebitslimited/edgealert_ban.json";
            private static List<string> _bannedODS;
            private static DateTime _lastFetched;
            private static readonly object _lock = new object();

            public static async Task<List<string>> GetBannedODSAsync()
            {
                if (_bannedODS == null || (DateTime.UtcNow - _lastFetched).TotalMinutes > 10)
                {
                    await RefreshBannedODSCache();
                }
                return _bannedODS;
            }

            public static async Task RefreshBannedODSCache()
            {
                lock (_lock)
                {
                    if (_bannedODS == null || (DateTime.UtcNow - _lastFetched).TotalMinutes > 10)
                    {
                        using (var httpClient = new HttpClient())
                        {
                            var json = httpClient.GetStringAsync(BanUrl).Result;
                            var bannedList = JsonSerializer.Deserialize<BannedList>(json);
                            _bannedODS = bannedList?.BannedODS ?? new List<string>();
                            _lastFetched = DateTime.UtcNow;
                        }
                    }
                }
            }

            public class BannedList
            {
                public List<string> BannedODS { get; set; }
            }
        }

        public class UpdateUserResponse
        {
            [JsonPropertyName("userName")]
            public string UserName { get; set; }

            [JsonPropertyName("isBlocked")]
            public bool IsBlocked { get; set; }

            [JsonPropertyName("message")]
            public string Message { get; set; }
        }

        // +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
        // +++ NEW HELPER FUNCTION +++
        // +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
        private static double GetDurationWithinCoreHours(DateTime stateStartUtc, double stateDurationSeconds, string state, string previousState, ILogger log)
        {
            if (stateDurationSeconds <= 0) return 0;

            state = state?.ToUpperInvariant() ?? "UNKNOWN";
            previousState = previousState?.ToUpperInvariant() ?? "UNKNOWN";

            // Active time always counts fully
            if (state == "ACTIVE")
            {
                return stateDurationSeconds;
            }

            DateTime stateEndUtc = stateStartUtc.AddSeconds(stateDurationSeconds);

            // Get UK time zone (works for both GMT and BST automatically)
            var ukTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");

            // Convert state start time to UK local time
            DateTime stateStartLocal = TimeZoneInfo.ConvertTimeFromUtc(stateStartUtc, ukTimeZone);

            // Use the local date to calculate core hours in UK time
            DateTime dayStartLocal = stateStartLocal.Date;
            DateTime coreStartLocal = dayStartLocal.Add(CoreHoursStart); // 8:00 UK time
            DateTime coreEndLocal = dayStartLocal.Add(CoreHoursEnd);     // 18:00 UK time

            // Convert core hours back to UTC for comparison
            DateTime coreStartUtc = TimeZoneInfo.ConvertTimeToUtc(coreStartLocal, ukTimeZone);
            DateTime coreEndUtc = TimeZoneInfo.ConvertTimeToUtc(coreEndLocal, ukTimeZone);

            // Continue with existing intersection calculation
            DateTime intersectionStart = stateStartUtc > coreStartUtc ? stateStartUtc : coreStartUtc;
            DateTime intersectionEnd = stateEndUtc < coreEndUtc ? stateEndUtc : coreEndUtc;

            double intersectionDuration = 0;
            if (intersectionEnd > intersectionStart)
            {
                intersectionDuration = (intersectionEnd - intersectionStart).TotalSeconds;
            }

            // Handle the exception for Offline state preceded by Active state
            if (state == "OFFLINE" && previousState == "ACTIVE")
            {
                double outsideCoreDuration = 0;
                // Check period before core hours start
                if (stateStartUtc < coreStartUtc)
                {
                    DateTime periodEnd = stateEndUtc < coreStartUtc ? stateEndUtc : coreStartUtc;
                    if (periodEnd > stateStartUtc)
                    {
                        outsideCoreDuration += (periodEnd - stateStartUtc).TotalSeconds;
                    }
                }
                // Check period after core hours end
                if (stateEndUtc > coreEndUtc)
                {
                    DateTime periodStart = stateStartUtc > coreEndUtc ? stateStartUtc : coreEndUtc;
                    if (stateEndUtc > periodStart)
                    {
                        outsideCoreDuration += (stateEndUtc - periodStart).TotalSeconds;
                    }
                }

                // Optional: Add debug logging
                log?.LogTrace($"OFFLINE preceded by ACTIVE: Core intersection={intersectionDuration:F1}s, Outside core={outsideCoreDuration:F1}s, Total={intersectionDuration + outsideCoreDuration:F1}s");

                return intersectionDuration + outsideCoreDuration;
            }
            else
            {
                // For Away, or Offline NOT preceded by Active
                // Optional: Add debug logging
                log?.LogTrace($"{state}: Core intersection={intersectionDuration:F1}s");

                return intersectionDuration;
            }
        }

        // --- New Function: LogUserTime ---

        [FunctionName("LogUserTime")]
        public static async Task<IActionResult> LogUserTime(
                           [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
                           ILogger log)
        {
            log.LogInformation("C# HTTP trigger function 'LogUserTime' (Ingestion Only) processed a request.");

            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogError("Azure Storage Connection String is not configured.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // Instantiate ONLY the detailed log table handler
            var userTimeLogTables = new UserTimeLogTables(connectionString);

            try
            {
                // Ensure detailed table exists
                await userTimeLogTables.CreateTableAsync();
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to ensure UserTimeLog table exists.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            string requestBody;
            try
            {
                requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    log.LogWarning("Received empty request body.");
                    return new BadRequestObjectResult("Request body cannot be empty.");
                }
                log.LogInformation($"Received request body: {requestBody}");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error reading request body.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // Deserialize into the server-side DTO
            List<ServerSideUserStateLogDto> clientLogEntries; // Use the server-side DTO
            try
            {
                // Configure options - robust casing, NO enum converter needed as State is string in DTO
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                clientLogEntries = JsonSerializer.Deserialize<List<ServerSideUserStateLogDto>>(requestBody, options);

                if (clientLogEntries == null || !clientLogEntries.Any())
                {
                    log.LogWarning("Deserialized client log entries list is null or empty.");
                    return new BadRequestObjectResult("Invalid or empty log entry list.");
                }
            }
            catch (JsonException jsonEx)
            {
                log.LogError(jsonEx, "Error deserializing request body JSON into ServerSideUserStateLogDto.");
                return new BadRequestObjectResult($"Invalid JSON format: {jsonEx.Message}");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Unexpected error during deserialization into ServerSideUserStateLogDto.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // Prepare list for batch insert
            var detailedLogsToAdd = new List<TableTransactionAction>();

            // Map from DTO to entity model
            foreach (var clientEntry in clientLogEntries)
            {
                // Basic Validation on DTO data
                if (string.IsNullOrWhiteSpace(clientEntry.UserName)
                    || string.IsNullOrWhiteSpace(clientEntry.MachineID)
                    || string.IsNullOrWhiteSpace(clientEntry.State) // Validate the State string
                    || clientEntry.StateStartTime == DateTime.MinValue)
                {
                    log.LogWarning($"Skipping invalid DTO entry: UserName={clientEntry.UserName}, MachineID={clientEntry.MachineID}, State={clientEntry.State}, StartTime={clientEntry.StateStartTime}");
                    continue;
                }

                // Convert UserName to Uppercase for consistency
                string upperCaseUserName = clientEntry.UserName.ToUpperInvariant();

                // Create the Entity and set PartitionKey/RowKey
                var entity = new UserTimeLogEntity
                {
                    ODS = clientEntry.ODS,
                    State = clientEntry.State, // Use the string state directly
                    DurationSeconds = clientEntry.DurationSeconds,
                    Context = clientEntry.Context,
                    StateStartTime = clientEntry.StateStartTime,
                    Timestamp = DateTimeOffset.UtcNow,
                    MachineID = clientEntry.MachineID,
                    UserName = upperCaseUserName, // Store uppercase UserName
                    PartitionKey = upperCaseUserName, // Use uppercase UserName as PartitionKey
                    RowKey = $"{DateTimeOffset.MaxValue.Ticks - clientEntry.StateStartTime.Ticks:D19}_{clientEntry.MachineID}"
                };

                // Add to batch
                detailedLogsToAdd.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace, entity));
            }

            // --- Perform Batch Insert ---
            if (detailedLogsToAdd.Any())
            {
                try
                {
                    log.LogInformation($"Attempting to batch insert {detailedLogsToAdd.Count} detailed log entries.");
                    await userTimeLogTables.SubmitTransactionInChunksAsync(detailedLogsToAdd, log);
                    log.LogInformation($"Successfully inserted {detailedLogsToAdd.Count} detailed log entries.");
                }
                catch (RequestFailedException rfEx)
                {
                    log.LogError(rfEx, $"Azure Table Storage error submitting detailed log transaction. Status: {rfEx.Status}, ErrorCode: {rfEx.ErrorCode}, Message: {rfEx.Message}");
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "General error submitting detailed log transaction.");
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
            }
            else
            {
                log.LogInformation("No valid detailed log entries to add after validation.");
            }

            return new OkObjectResult("Log entries received.");
        }

        // --- New Timer-Triggered Function for Nightly Summarization and Purge ---

        [FunctionName("SummarizeAndPurgeLogs")]
        public static async Task SummarizeAndPurgeLogs(
                           [TimerTrigger("0 0 2 * * *")] TimerInfo myTimer, // Runs at 2:00 AM UTC
                           ILogger log)
        {
            log.LogInformation($"C# Timer trigger function 'SummarizeAndPurgeLogs' executed at: {DateTime.UtcNow}");
            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogError("Azure Storage Connection String is not configured. Aborting nightly job.");
                return;
            }

            var detailedLogTable = new UserTimeLogTables(connectionString);
            var summaryTable = new UserTimeSummaryTables(connectionString);

            // --- Define Retention Periods ---
            int detailedRetentionDays = 30; // Keep detailed logs for this many days
            int summaryRetentionDays = 95; // Keep summary logs for this many days

            DateTimeOffset detailedCutoff = DateTimeOffset.UtcNow.AddDays(-detailedRetentionDays);
            DateTimeOffset summaryPurgeCutoff = DateTimeOffset.UtcNow.AddDays(-summaryRetentionDays);

            log.LogInformation($"Detailed log retention cutoff (Summarize data older than): {detailedCutoff}");
            log.LogInformation($"Summary log purge cutoff (Purge summaries older than): {summaryPurgeCutoff}");

            try
            {
                // --- 1. Summarize Logs Older than Detailed Retention ---
                log.LogInformation($"Starting summarization process for logs older than {detailedCutoff}...");
                var logsToSummarize = await detailedLogTable.GetLogsOlderThanAsync(detailedCutoff, log);

                if (!logsToSummarize.Any())
                {
                    log.LogInformation("No detailed logs found older than retention period to summarize.");
                }
                else
                {
                    log.LogInformation($"Found {logsToSummarize.Count} detailed log entries to summarize.");
                    var groupedForSummary = logsToSummarize
                        .GroupBy(l => new { l.PartitionKey, Date = l.StateStartTime.Date, l.ODS })
                        .ToList();
                    log.LogInformation($"Grouped logs into {groupedForSummary.Count} user/day/ODS combinations for summarization.");

                    var summaryActions = new List<TableTransactionAction>();
                    foreach (var group in groupedForSummary)
                    {
                        double activeSec = 0;
                        double awaySec = 0;
                        double offlineSec = 0;
                        string odsCode = group.Key.ODS ?? "UNKNOWN";

                        var logsForDay = group.OrderBy(l => l.StateStartTime).ToList();
                        if (!logsForDay.Any()) continue;

                        var firstLogOfDay = logsForDay.First();
                        var startOfDayUtc = firstLogOfDay.StateStartTime.Date; // Midnight UTC
                        var durationSinceMidnight = (firstLogOfDay.StateStartTime - startOfDayUtc).TotalSeconds;
                        string statePrecedingMidnight = "UNKNOWN"; // Define outside the loops

                        // --- Handle time from Midnight to First Log ---
                        if (durationSinceMidnight > 0)
                        {
                            string firstState = firstLogOfDay.State?.ToUpperInvariant();
                            string firstContext = firstLogOfDay.Context;
                            string assumedStateSinceMidnight = "OFFLINE"; // Default

                            // Your logic to determine assumedStateSinceMidnight
                            if (firstContext == "Application Start") { assumedStateSinceMidnight = "OFFLINE"; }
                            else if (firstState == "ACTIVE") { assumedStateSinceMidnight = "AWAY"; }
                            else if (firstState == "AWAY") { assumedStateSinceMidnight = "ACTIVE"; }
                            else if (firstState == "OFFLINE") { assumedStateSinceMidnight = "AWAY"; }
                            else { assumedStateSinceMidnight = "OFFLINE"; }

                            // Apply core hours logic to the *assumed* state
                            if (assumedStateSinceMidnight == "ACTIVE")
                            {
                                activeSec += GetDurationWithinCoreHours(startOfDayUtc, durationSinceMidnight, "ACTIVE", statePrecedingMidnight, log);
                            }
                            else if (assumedStateSinceMidnight == "AWAY")
                            {
                                awaySec += GetDurationWithinCoreHours(startOfDayUtc, durationSinceMidnight, "AWAY", statePrecedingMidnight, log);
                            }
                            else if (assumedStateSinceMidnight == "OFFLINE")
                            {
                                offlineSec += GetDurationWithinCoreHours(startOfDayUtc, durationSinceMidnight, "OFFLINE", statePrecedingMidnight, log);
                            }
                            log.LogInformation($"User '{group.Key.PartitionKey}' on {group.Key.Date:yyyy-MM-dd}: Assumed '{assumedStateSinceMidnight}' for {durationSinceMidnight:F1}s since midnight.");
                        }

                        // --- Process Durations for Each Log Entry IN the Day ---
                        for (int i = 0; i < logsForDay.Count; i++)
                        {
                            var currentLog = logsForDay[i];
                            DateTime stateStartTime = currentLog.StateStartTime;
                            string currentState = currentLog.State?.ToUpperInvariant() ?? "UNKNOWN";
                            // State *before* this one started
                            string previousState = (i > 0) ? logsForDay[i - 1].State?.ToUpperInvariant() : statePrecedingMidnight; // Use statePrecedingMidnight for the first log

                            // Determine the end time for the currentLog's state
                            DateTime stateEndTimeUtc;
                            if (i + 1 < logsForDay.Count)
                            {
                                stateEndTimeUtc = logsForDay[i + 1].StateStartTime; // Ends when next state starts
                            }
                            else
                            {
                                // Last log for the day being summarized (always a past day here)
                                stateEndTimeUtc = group.Key.Date.AddDays(1); // Midnight UTC of the next day
                            }

                            // Calculate the actual duration of the current state
                            double actualDurationSeconds = 0;
                            if (stateEndTimeUtc > stateStartTime)
                            {
                                actualDurationSeconds = (stateEndTimeUtc - stateStartTime).TotalSeconds;
                            }
                            else
                            {
                                log.LogWarning($"User '{group.Key.PartitionKey}' on {group.Key.Date:yyyy-MM-dd}: Skipping duration for state '{currentState}' starting {stateStartTime:o} as end time {stateEndTimeUtc:o} is not later.");
                            }

                            // Apply core hours logic using the *actual* duration and the *previous* state
                            if (actualDurationSeconds > 0)
                            {
                                if (currentState == "ACTIVE")
                                {
                                    activeSec += GetDurationWithinCoreHours(stateStartTime, actualDurationSeconds, "ACTIVE", previousState, log);
                                }
                                else if (currentState == "AWAY")
                                {
                                    awaySec += GetDurationWithinCoreHours(stateStartTime, actualDurationSeconds, "AWAY", previousState, log);
                                }
                                else if (currentState == "OFFLINE")
                                {
                                    offlineSec += GetDurationWithinCoreHours(stateStartTime, actualDurationSeconds, "OFFLINE", previousState, log);
                                }
                                // Optional: Detailed logging for debugging
                                // log.LogInformation($"User '{group.Key.PartitionKey}' on {group.Key.Date:yyyy-MM-dd}: State '{currentState}' from {stateStartTime:HH:mm:ss} to {stateEndTimeUtc:HH:mm:ss} ({actualDurationSeconds:F1}s). Prev: '{previousState}'. Core Active: {activeSec:F0}, Away: {awaySec:F0}, Offline: {offlineSec:F0}");
                            }
                        }

                        // --- Create Summary Entry ---
                        var summaryEntry = new UserTimeSummaryEntity
                        {
                            PartitionKey = group.Key.PartitionKey,
                            RowKey = $"{group.Key.Date:yyyyMMdd}_{odsCode}",
                            UserID = group.Key.PartitionKey,
                            ODS = odsCode,
                            Date = group.Key.Date,
                            TotalActiveSeconds = Math.Round(activeSec, 1),
                            TotalAwaySeconds = Math.Round(awaySec, 1),
                            TotalOfflineSeconds = Math.Round(offlineSec, 1),
                            Timestamp = DateTimeOffset.UtcNow
                        };
                        summaryActions.Add(new TableTransactionAction(TableTransactionActionType.UpsertMerge, summaryEntry));
                        log.LogInformation($"Summary for {group.Key.PartitionKey} on {group.Key.Date:yyyy-MM-dd} (ODS:{odsCode}): A={activeSec:F0}, W={awaySec:F0}, O={offlineSec:F0}");

                    } // End foreach group

                    // Submit summary updates in batches
                    if (summaryActions.Any())
                    {
                        log.LogInformation($"Submitting {summaryActions.Count} summary updates...");
                        await summaryTable.SubmitTransactionInChunksAsync(summaryActions, log);
                        log.LogInformation("Summary updates submitted.");

                        // --- 2. Purge Summarized Logs from Detailed Table ---
                        log.LogInformation("Preparing to purge summarized detailed logs...");
                        // *** CORRECTED ETAG HANDLING FOR DELETE ***
                        var deleteActions = logsToSummarize
                            .Select(logToDelete => {
                                // Create an entity with the correct PK/RK and set ETag for delete
                                var entityToDelete = new UserTimeLogEntity
                                {
                                    PartitionKey = logToDelete.PartitionKey,
                                    RowKey = logToDelete.RowKey,
                                    ETag = ETag.All // Use wildcard ETag for unconditional delete
                                };
                                return new TableTransactionAction(TableTransactionActionType.Delete, entityToDelete);
                            })
                            .ToList();
                        if (deleteActions.Any())
                        {
                            log.LogInformation($"Submitting {deleteActions.Count} delete actions for detailed logs...");
                            await detailedLogTable.SubmitTransactionInChunksAsync(deleteActions, log);
                            log.LogInformation("Purge of summarized detailed logs submitted.");
                        }
                        else
                        {
                            log.LogInformation("No detailed logs needed purging after summarization.");
                        }
                    }
                    else
                    {
                        log.LogInformation("No summary actions generated.");
                    }
                } // End if logsToSummarize.Any()

                // --- 3. Purge Old Summaries ---
                log.LogInformation($"Starting purge of summaries older than {summaryPurgeCutoff}...");
                var summariesToPurge = await summaryTable.GetSummaryLogsAsync(DateTime.MinValue, summaryPurgeCutoff.Date.AddDays(-1), null, null, log);

                if (!summariesToPurge.Any())
                {
                    log.LogInformation("No summary logs found older than retention period to purge.");
                }
                else
                {
                    log.LogInformation($"Found {summariesToPurge.Count} old summary logs to purge.");
                    // *** CORRECTED ETAG HANDLING FOR DELETE ***
                    var deleteSummaryActions = summariesToPurge
                        .Select(summaryToDelete => {
                            // Create an entity with the correct PK/RK and set ETag for delete
                            var entityToDelete = new UserTimeSummaryEntity
                            {
                                PartitionKey = summaryToDelete.PartitionKey,
                                RowKey = summaryToDelete.RowKey,
                                ETag = ETag.All // Use wildcard ETag for unconditional delete
                            };
                            return new TableTransactionAction(TableTransactionActionType.Delete, entityToDelete);
                        })
                        .ToList();
                    if (deleteSummaryActions.Any())
                    {
                        log.LogInformation($"Submitting {deleteSummaryActions.Count} delete actions for old summaries...");
                        await summaryTable.SubmitTransactionInChunksAsync(deleteSummaryActions, log);
                        log.LogInformation("Purge of old summary logs submitted.");
                    }
                }

                log.LogInformation($"SummarizeAndPurgeLogs function finished successfully at: {DateTime.UtcNow}");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "An error occurred during the SummarizeAndPurgeLogs function.");
            }
        }

        private const string SETTING_PARTITION_KEY = "TimeTracking"; // Define a partition key for these settings
        private const string AWAY_TIMEOUT_SETTING_NAME = "AwayTimeoutMinutes"; // Define the specific setting name

        [FunctionName("GetSetting")]
        public static async Task<IActionResult> GetSetting(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "settings/{settingName}")] HttpRequest req,
            string settingName, // Capture setting name from the route
            ILogger log)
        {
            log.LogInformation($"C# HTTP trigger function 'GetSetting' processed a request for '{settingName}'.");

            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogError("Azure Storage Connection String is not configured.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            var settingsTable = new GlobalSettingsTable(connectionString, log);
            await settingsTable.CreateTableAsync(); // Ensure table exists

            // Use predefined PartitionKey, get RowKey (settingName) from route
            string value = await settingsTable.GetSettingAsync(SETTING_PARTITION_KEY, settingName);

            if (value != null)
            {
                // Return the value simply as plain text or JSON
                // return new OkObjectResult(new { Name = settingName, Value = value });
                return new OkObjectResult(value); // Return just the value string
            }
            else
            {
                // Return a default if not found, or indicate not found
                // Example: Return a default value if specifically getting AwayTimeout
                if (settingName.Equals(AWAY_TIMEOUT_SETTING_NAME, StringComparison.OrdinalIgnoreCase))
                {
                    log.LogWarning($"{AWAY_TIMEOUT_SETTING_NAME} not found, returning default '3'.");
                    return new OkObjectResult("3"); // Default value
                }
                return new NotFoundObjectResult($"Setting '{settingName}' not found.");
            }
        }

        [FunctionName("UpdateSetting")]
        public static async Task<IActionResult> UpdateSetting(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "settings/{settingName}")] HttpRequest req,
            string settingName, // Capture setting name from the route
            ILogger log)
        {
            log.LogInformation($"C# HTTP trigger function 'UpdateSetting' processed a request for '{settingName}'.");

            // TODO: Implement proper Admin authorization check here in a real scenario

            string requestBody;
            try
            {
                requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    return new BadRequestObjectResult("Request body cannot be empty. Expecting a JSON object like {\"value\":\"newValue\"}.");
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error reading request body for UpdateSetting.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            string newValue;
            try
            {
                // Expecting a simple JSON like {"value": "5"}
                using (JsonDocument document = JsonDocument.Parse(requestBody))
                {
                    if (!document.RootElement.TryGetProperty("value", out JsonElement valueElement) || valueElement.ValueKind != JsonValueKind.String)
                    {
                        return new BadRequestObjectResult("Invalid JSON format. Expecting {\"value\":\"newValue\"}.");
                    }
                    newValue = valueElement.GetString();
                }
                if (string.IsNullOrWhiteSpace(newValue))
                {
                    return new BadRequestObjectResult("The 'value' property cannot be empty.");
                }

                // Specific validation if updating the Away Timeout
                if (settingName.Equals(AWAY_TIMEOUT_SETTING_NAME, StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(newValue, out int minutes) || minutes < 1 || minutes > 60) // Example validation: 1-60 minutes
                    {
                        return new BadRequestObjectResult("Invalid value for AwayTimeoutMinutes. Must be an integer between 1 and 60.");
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                log.LogError(jsonEx, "Error deserializing request body JSON for UpdateSetting.");
                return new BadRequestObjectResult($"Invalid JSON format: {jsonEx.Message}");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Unexpected error during request body processing for UpdateSetting.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }


            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogError("Azure Storage Connection String is not configured.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            var settingsTable = new GlobalSettingsTable(connectionString, log);
            await settingsTable.CreateTableAsync(); // Ensure table exists

            // Use predefined PartitionKey, get RowKey (settingName) from route
            bool success = await settingsTable.UpdateSettingAsync(SETTING_PARTITION_KEY, settingName, newValue);

            if (success)
            {
                return new OkObjectResult($"Setting '{settingName}' updated successfully.");
            }
            else
            {
                return new StatusCodeResult(StatusCodes.Status500InternalServerError); // Or more specific error based on UpdateSettingAsync failure
            }
        }


        // Helper function to check permissions
        private static async Task<bool> CheckReportPermission(string requestingUserId, string targetOds, ILogger log)
        {
            // Ensure targetOds is not the wildcard itself for this check
            if (targetOds == "*")
            {
                log.LogWarning($"CheckReportPermission: Attempted to check permission for wildcard '*'. This is not a valid target ODS for checking specific access.");
                // Depending on requirements, you might return false or handle differently.
                // Returning false assumes you need explicit permission for a *real* ODS.
                return false;
            }

            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogError("CheckReportPermission: Azure Storage Connection String is not configured.");
                return false; // Cannot check permissions
            }
            var accessTable = new UserReportAccessTables(connectionString, log);

            // 1. Check ONLY for specific ODS permission
            var specificPermission = await accessTable.GetPermissionAsync(requestingUserId, targetOds);

            // Check if a specific permission exists and is valid ('Viewer' or 'Admin')
            if (specificPermission != null && (specificPermission.PermissionLevel == "Viewer" || specificPermission.PermissionLevel == "Admin"))
            {
                log.LogInformation($"CheckReportPermission: User '{requestingUserId}' has '{specificPermission.PermissionLevel}' access to specific ODS '{targetOds}'. Access granted.");
                return true;
            }

            // If specific permission wasn't found (or wasn't valid), deny access.
            log.LogWarning($"CheckReportPermission: User '{requestingUserId}' has NO specific report access permission for ODS '{targetOds}'. Access denied.");
            return false;
        }

        // 1. Modified CheckReportPermission Function
        private static async Task<List<string>> GetPermittedOdsCodesAsync(string requestingUserId, ILogger log)
        {
            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogError("GetPermittedOdsCodesAsync: Azure Storage Connection String is not configured.");
                return new List<string>(); // Return empty list on error
            }
            var accessTable = new UserReportAccessTables(connectionString, log);
            try
            {
                var permissions = await accessTable.GetPermissionsForUserAsync(requestingUserId);
                if (permissions == null || !permissions.Any())
                {
                    log.LogWarning($"GetPermittedOdsCodesAsync: User '{requestingUserId}' has NO permissions.");
                    return new List<string>();
                }

                // Extract distinct, non-null/empty ODS codes
                var odsCodes = permissions
                                .Select(p => p.TargetODS)
                                .Where(ods => !string.IsNullOrWhiteSpace(ods))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();

                log.LogInformation($"GetPermittedOdsCodesAsync: User '{requestingUserId}' has permission for ODS codes: {string.Join(", ", odsCodes)}");
                return odsCodes;
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"GetPermittedOdsCodesAsync: Error retrieving permissions for user '{requestingUserId}'.");
                return new List<string>(); // Return empty list on error
            }
        }

        // 2. Modified GetSummarizedReportData Function
        // Modified GetSummarizedReportData Function
        [FunctionName("GetSummarizedReportData")]
        public static async Task<IActionResult> GetSummarizedReportData(
                   [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
                    ILogger log)
        {
            log.LogInformation("C# HTTP trigger function 'GetSummarizedReportData' processed a request.");

            // --- Authentication/Authorization ---
            string userIdFromHeader = req.Headers.ContainsKey("X-Authenticated-User-Id") ? req.Headers["X-Authenticated-User-Id"].FirstOrDefault() : null;
            string requestingUserName = null;
            if (!string.IsNullOrWhiteSpace(userIdFromHeader))
            {
                // Split at the FIRST hyphen to separate UserName from HostName
                int separatorIndex = userIdFromHeader.IndexOf('-'); // Use IndexOf
                requestingUserName = (separatorIndex > 0) ? userIdFromHeader.Substring(0, separatorIndex) : userIdFromHeader;
                if (separatorIndex > 0)
                {
                    log.LogInformation($"GetSummarizedReportData: Parsed UserName '{requestingUserName}' from header '{userIdFromHeader}' by splitting at the first hyphen.");
                }
                else
                {
                    log.LogWarning($"GetSummarizedReportData: Could not find a hyphen to parse UserName from header '{userIdFromHeader}'. Using full value as UserName for lookup.");
                }
            }
            if (string.IsNullOrWhiteSpace(requestingUserName))
            {
                log.LogWarning("GetSummarizedReportData: Requesting user name could not be determined. Unauthorized.");
                return new UnauthorizedResult();
            }
            log.LogInformation($"GetSummarizedReportData: Request received from user: {requestingUserName}");

            List<string> permittedOdsCodes = await GetPermittedOdsCodesAsync(requestingUserName, log);
            if (!permittedOdsCodes.Any())
            {
                log.LogWarning($"GetSummarizedReportData: User '{requestingUserName}' has no permissions. Returning empty list.");
                return new OkObjectResult(new List<UserTimeSummaryEntity>());
            }

            // --- Get Filters ---
            string filterUser = req.Query["user"];
            string filterOds = req.Query["ods"];
            string filterStartDateStr = req.Query["startDate"];
            string filterEndDateStr = req.Query["endDate"];

            if (!DateTime.TryParse(filterStartDateStr, out DateTime filterStartDate) ||
                !DateTime.TryParse(filterEndDateStr, out DateTime filterEndDate))
            {
                return new BadRequestObjectResult("Invalid or missing 'startDate' or 'endDate'. Use yyyy-MM-dd format.");
            }
            // Use DateTimeOffset for internal date comparisons
            DateTimeOffset filterStartDateOffset = new DateTimeOffset(filterStartDate.Date, TimeSpan.Zero); // Start of day UTC
            DateTimeOffset filterEndDateOffset = new DateTimeOffset(filterEndDate.Date.AddDays(1).AddTicks(-1), TimeSpan.Zero); // End of day UTC

            // --- Permission Check ---
            if (!string.IsNullOrWhiteSpace(filterOds) && !permittedOdsCodes.Contains(filterOds, StringComparer.OrdinalIgnoreCase))
            {
                log.LogWarning($"GetSummarizedReportData: User '{requestingUserName}' does not have permission for ODS '{filterOds}'. Forbidden.");
                return new ForbidResult();
            }

            // --- Data Retrieval ---
            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogError("GetSummarizedReportData: Connection String not configured.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            var summaryTable = new UserTimeSummaryTables(connectionString);
            var detailedTable = new UserTimeLogTables(connectionString);
            var combinedResultsDict = new Dictionary<string, UserTimeSummaryEntity>(); // Use Dict for efficient merging: Key = PK_RowKey
            DateTimeOffset ninetyDaysAgo = DateTimeOffset.UtcNow.AddDays(-90); // Approx retention boundary

            try
            {
                List<string> odsToQuery = string.IsNullOrWhiteSpace(filterOds) ? permittedOdsCodes : new List<string> { filterOds };

                foreach (var odsCode in odsToQuery)
                {
                    // 1. Get existing summaries from UserTimeSummary table
                    var summaryLogs = await summaryTable.GetSummaryLogsAsync(filterStartDate.Date, filterEndDate.Date, filterUser, odsCode, log);
                    foreach (var summary in summaryLogs)
                    {
                        string dictKey = $"{summary.PartitionKey}_{summary.RowKey}";
                        if (!combinedResultsDict.ContainsKey(dictKey))
                        {
                            combinedResultsDict.Add(dictKey, summary);
                        }
                    }
                    log.LogInformation($"Retrieved {summaryLogs.Count} existing summary entries for ODS {odsCode}.");

                    // 2. Get recent detailed logs (within 90 days AND within filter dates) for this ODS
                    DateTimeOffset detailedStartDateToFetch = filterStartDateOffset < ninetyDaysAgo ? ninetyDaysAgo : filterStartDateOffset;

                    if (detailedStartDateToFetch <= filterEndDateOffset)
                    {
                        var detailedLogsRaw = await detailedTable.GetDetailedLogsAsync(detailedStartDateToFetch, filterEndDateOffset, filterUser, null, log);
                        var detailedLogsForOds = detailedLogsRaw
                                                   .Where(l => l.ODS != null && string.Equals(l.ODS, odsCode, StringComparison.OrdinalIgnoreCase))
                                                   .ToList();
                        log.LogInformation($"Retrieved {detailedLogsForOds.Count} RECENT detailed entries matching ODS {odsCode} for on-the-fly aggregation.");

                        // Aggregate these recent detailed logs using the CORRECTED logic
                        var aggregatedDetailed = detailedLogsForOds
                            .GroupBy(l => new { l.PartitionKey, Date = l.StateStartTime.Date })
                            .Select(g =>
                            {
                                double activeSec = 0; double awaySec = 0; double offlineSec = 0;
                                var logsForDay = g.OrderBy(l => l.StateStartTime).ToList();
                                if (!logsForDay.Any()) return null;

                                var firstLogOfDay = logsForDay.First();
                                var startOfDayUtc = firstLogOfDay.StateStartTime.Date;
                                var durationSinceMidnight = (firstLogOfDay.StateStartTime - startOfDayUtc).TotalSeconds;
                                string statePrecedingMidnight = "UNKNOWN";

                                // --- Handle time from Midnight to First Log ---
                                if (durationSinceMidnight > 0)
                                {
                                    string firstState = firstLogOfDay.State?.ToUpperInvariant();
                                    string firstContext = firstLogOfDay.Context;
                                    string assumedStateSinceMidnight = "OFFLINE"; // Default

                                    if (firstContext == "Application Start") { assumedStateSinceMidnight = "OFFLINE"; }
                                    else if (firstState == "ACTIVE") { assumedStateSinceMidnight = "AWAY"; }
                                    else if (firstState == "AWAY") { assumedStateSinceMidnight = "ACTIVE"; }
                                    else if (firstState == "OFFLINE") { assumedStateSinceMidnight = "AWAY"; }
                                    else { assumedStateSinceMidnight = "OFFLINE"; }

                                    if (assumedStateSinceMidnight == "ACTIVE")
                                    {
                                        activeSec += GetDurationWithinCoreHours(startOfDayUtc, durationSinceMidnight, "ACTIVE", statePrecedingMidnight, log);
                                    }
                                    else if (assumedStateSinceMidnight == "AWAY")
                                    {
                                        awaySec += GetDurationWithinCoreHours(startOfDayUtc, durationSinceMidnight, "AWAY", statePrecedingMidnight, log);
                                    }
                                    else if (assumedStateSinceMidnight == "OFFLINE")
                                    {
                                        offlineSec += GetDurationWithinCoreHours(startOfDayUtc, durationSinceMidnight, "OFFLINE", statePrecedingMidnight, log);
                                    }
                                    log.LogInformation($"User '{g.Key.PartitionKey}' on {g.Key.Date:yyyy-MM-dd}: Assumed '{assumedStateSinceMidnight}' for {durationSinceMidnight:F1}s since midnight (Recent).");
                                }

                                // --- Process Durations for Each Log Entry IN the Day ---
                                for (int i = 0; i < logsForDay.Count; i++)
                                {
                                    var currentLog = logsForDay[i];
                                    DateTime stateStartTime = currentLog.StateStartTime;
                                    string currentState = currentLog.State?.ToUpperInvariant() ?? "UNKNOWN";
                                    string previousState = (i > 0) ? logsForDay[i - 1].State?.ToUpperInvariant() : statePrecedingMidnight;

                                    DateTime stateEndTimeUtc;
                                    if (i + 1 < logsForDay.Count)
                                    {
                                        stateEndTimeUtc = logsForDay[i + 1].StateStartTime;
                                    }
                                    else
                                    {
                                        // Last log for the day being processed
                                        DateTime effectiveEndOfPeriod;
                                        DateTime currentUtcDate = DateTime.UtcNow.Date;
                                        DateTime groupDate = g.Key.Date;

                                        if (groupDate == currentUtcDate)
                                        {
                                            effectiveEndOfPeriod = DateTime.UtcNow;
                                            if (effectiveEndOfPeriod > filterEndDateOffset.UtcDateTime) { effectiveEndOfPeriod = filterEndDateOffset.UtcDateTime; }
                                        }
                                        else
                                        {
                                            effectiveEndOfPeriod = groupDate.AddDays(1); // Midnight UTC
                                            if (effectiveEndOfPeriod > filterEndDateOffset.UtcDateTime) { effectiveEndOfPeriod = filterEndDateOffset.UtcDateTime; }
                                        }
                                        stateEndTimeUtc = effectiveEndOfPeriod;
                                    }

                                    double actualDurationSeconds = 0;
                                    if (stateEndTimeUtc > stateStartTime)
                                    {
                                        actualDurationSeconds = (stateEndTimeUtc - stateStartTime).TotalSeconds;
                                    }
                                    else
                                    {
                                        log.LogWarning($"User '{g.Key.PartitionKey}' on {g.Key.Date:yyyy-MM-dd}: Skipping duration for state '{currentState}' starting {stateStartTime:o} as end time {stateEndTimeUtc:o} is not later (Recent).");
                                    }

                                    if (actualDurationSeconds > 0)
                                    {
                                        if (currentState == "ACTIVE")
                                        {
                                            activeSec += GetDurationWithinCoreHours(stateStartTime, actualDurationSeconds, "ACTIVE", previousState, log);
                                        }
                                        else if (currentState == "AWAY")
                                        {
                                            awaySec += GetDurationWithinCoreHours(stateStartTime, actualDurationSeconds, "AWAY", previousState, log);
                                        }
                                        else if (currentState == "OFFLINE")
                                        {
                                            offlineSec += GetDurationWithinCoreHours(stateStartTime, actualDurationSeconds, "OFFLINE", previousState, log);
                                        }
                                    }
                                }

                                // --- Create the temporary summary entity ---
                                return new UserTimeSummaryEntity
                                {
                                    PartitionKey = g.Key.PartitionKey,
                                    RowKey = $"{g.Key.Date:yyyyMMdd}_{odsCode}", // Ensure RowKey matches format
                                    UserID = g.Key.PartitionKey,
                                    ODS = odsCode,
                                    Date = g.Key.Date,
                                    TotalActiveSeconds = Math.Round(activeSec, 1),
                                    TotalAwaySeconds = Math.Round(awaySec, 1),
                                    TotalOfflineSeconds = Math.Round(offlineSec, 1),
                                    Timestamp = DateTimeOffset.UtcNow // Indicate it's freshly calculated
                                };
                            })
                            .Where(s => s != null)
                            .ToList();

                        // Merge recent detailed aggregations into the dictionary
                        foreach (var detailedAgg in aggregatedDetailed)
                        {
                            string dictKey = $"{detailedAgg.PartitionKey}_{detailedAgg.RowKey}";
                            if (combinedResultsDict.TryGetValue(dictKey, out var existingSummary))
                            {
                                // If a summary from UserTimeSummary table exists, DO NOT overwrite.
                                // The nightly job is the source of truth for older data summaries.
                                // We only add *new* summaries calculated from recent detailed logs
                                // if they weren't already present from the UserTimeSummary query.
                                log.LogTrace($"Skipping merge for {dictKey} as summary already exists from UserTimeSummary table.");
                            }
                            else
                            {
                                // If no summary exists for this day, add the freshly aggregated one
                                combinedResultsDict.Add(dictKey, detailedAgg);
                                log.LogInformation($"Added newly aggregated summary for {dictKey} from recent detailed logs.");
                            }
                        }
                        log.LogInformation($"Processed {aggregatedDetailed.Count} aggregated daily summaries from RECENT detailed logs for ODS {odsCode}.");
                    } // End if recent detailed logs exist for period
                } // End foreach odsCode

                // --- Return Results ---
                var finalResults = combinedResultsDict.Values.ToList();
                log.LogInformation($"Total combined results for user {requestingUserName}: {finalResults.Count}");
                return new OkObjectResult(finalResults.OrderBy(r => r.PartitionKey).ThenBy(r => r.Date));
            }
            catch (Exception ex)
            {
                log.LogError(ex, "An error occurred while retrieving or processing summarized report data.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // 3. Modified GetDetailedReportData Function
        [FunctionName("GetDetailedReportData")]
        public static async Task<IActionResult> GetDetailedReportData(
                    [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
                    ILogger log)
        {
            log.LogInformation("C# HTTP trigger function 'GetDetailedReportData' processed a request.");

            // --- Authentication/Authorization Header Parsing ---
            string userIdFromHeader = req.Headers.ContainsKey("X-Authenticated-User-Id") ? req.Headers["X-Authenticated-User-Id"].FirstOrDefault() : null;
            string requestingUserName = null;
            if (!string.IsNullOrWhiteSpace(userIdFromHeader))
            {
                // Split at the FIRST hyphen to separate UserName from HostName
                int separatorIndex = userIdFromHeader.IndexOf('-'); // Use IndexOf
                requestingUserName = (separatorIndex > 0) ? userIdFromHeader.Substring(0, separatorIndex) : userIdFromHeader;
                if (separatorIndex > 0)
                {
                    requestingUserName = userIdFromHeader.Substring(0, separatorIndex);
                }
                else
                {
                    // Fallback or handle cases where format might be different
                    requestingUserName = userIdFromHeader; // Or log an error
                    log.LogWarning($"GetDetailedReportData: Could not parse UserName from header '{userIdFromHeader}'. Using full value.");
                }
            }

            // Check if UserName could be extracted
            if (string.IsNullOrWhiteSpace(requestingUserName))
            {
                log.LogWarning("GetDetailedReportData: Requesting user name could not be determined from header. Unauthorized.");
                return new UnauthorizedResult();
            }
            log.LogInformation($"GetDetailedReportData: Request received from user: {requestingUserName} (Parsed from: {userIdFromHeader})");

            // --- Get Permitted ODS Codes using the parsed UserName ---
            List<string> permittedOdsCodes = await GetPermittedOdsCodesAsync(requestingUserName, log); // Use helper from previous step
            if (!permittedOdsCodes.Any())
            {
                log.LogWarning($"GetDetailedReportData: User '{requestingUserName}' has no permissions. Returning empty list.");
                return new OkObjectResult(new List<UserTimeLogEntity>()); // Return empty list if no permissions
            }

            // --- Get Filters ---
            string filterUser = req.Query["user"]; // Optional: User to filter for
            string filterMachine = req.Query["machine"]; // Optional: Machine ID to filter for
            string filterOds = req.Query["ods"]; // Specific ODS requested by client, or null if "All"
            string filterStartDateStr = req.Query["startDate"]; // Required: yyyy-MM-dd
            string filterEndDateStr = req.Query["endDate"]; // Required: yyyy-MM-dd

            if (!DateTimeOffset.TryParse(filterStartDateStr, out DateTimeOffset filterStartDate) ||
                !DateTimeOffset.TryParse(filterEndDateStr, out DateTimeOffset filterEndDate))
            {
                return new BadRequestObjectResult("Invalid or missing 'startDate' or 'endDate'. Use yyyy-MM-dd format.");
            }

            // Ensure full day coverage
            filterStartDate = filterStartDate.Date; // Beginning of start date (UTC)
            filterEndDate = filterEndDate.Date.AddDays(1).AddTicks(-1); // End of end date (UTC)

            // --- Permission Check for Specific ODS Request ---
            // If the client requested a specific ODS, check if it's in the permitted list
            if (!string.IsNullOrWhiteSpace(filterOds) && !permittedOdsCodes.Contains(filterOds, StringComparer.OrdinalIgnoreCase))
            {
                log.LogWarning($"GetDetailedReportData: User '{requestingUserName}' does not have permission for requested ODS '{filterOds}'. Forbidden.");
                return new ForbidResult();
            }

            // --- Data Retrieval ---
            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogError("GetDetailedReportData: Azure Storage Connection String not configured.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            var detailedTable = new UserTimeLogTables(connectionString); // Assumes UserTimeLogTables class is defined
            try
            {
                // Get detailed logs based on date/user/machine filters.
                // ODS filtering will happen *after* retrieval based on permittedOdsCodes.
                var allDetailedLogs = await detailedTable.GetDetailedLogsAsync(filterStartDate, filterEndDate, filterUser, filterMachine, log);

                // Determine which ODS codes to include in the final result based on request and permissions
                List<string> odsToIncludeInResult = string.IsNullOrWhiteSpace(filterOds)
                                                        ? permittedOdsCodes // User wants "All" permitted ODS
                                                        : new List<string> { filterOds }; // User wants a specific permitted ODS

                // Filter the results based on the ODS codes the user is allowed to see AND requested
                var filteredLogs = allDetailedLogs
                                    .Where(l => l.ODS != null && odsToIncludeInResult.Contains(l.ODS, StringComparer.OrdinalIgnoreCase))
                                    .ToList();

                log.LogInformation($"Returning {filteredLogs.Count} detailed log entries after permission filtering.");

                // --- Return Results ---
                // Return sorted by user, then by time (descending due to reverse ticks in RowKey)
                return new OkObjectResult(filteredLogs.OrderBy(l => l.PartitionKey).ThenBy(l => l.RowKey));
            }
            catch (Exception ex)
            {
                log.LogError(ex, "An error occurred while retrieving detailed report data.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("GetPermittedOdsForUser")]
        public static async Task<IActionResult> GetPermittedOdsForUser(
                    [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
                    ILogger log)
        {
            log.LogInformation("C# HTTP trigger function 'GetPermittedOdsForUser' processed a request.");
            string userIdFromHeader = req.Headers.ContainsKey("X-Authenticated-User-Id") ? req.Headers["X-Authenticated-User-Id"].FirstOrDefault() : null;
            string requestingUserName = null; // Variable to store the parsed UserName

            if (string.IsNullOrWhiteSpace(userIdFromHeader))
            {
                log.LogWarning("GetPermittedOdsForUser: Requesting user ID not found in headers. Unauthorized.");
                return new UnauthorizedResult();
            }

            int separatorIndex = userIdFromHeader.IndexOf('-'); // First occurrence
            if (separatorIndex > 0)
            {
                requestingUserName = userIdFromHeader.Substring(0, separatorIndex);
                log.LogInformation($"GetPermittedOdsForUser: Parsed UserName '{requestingUserName}' from header '{userIdFromHeader}'.");
            }
            else
            {
                // Handle cases where the header format might be unexpected
                requestingUserName = userIdFromHeader; // Use the full header as fallback
                log.LogWarning($"GetPermittedOdsForUser: Could not parse UserName from header '{userIdFromHeader}'. Using full value as UserName for lookup.");
            }


            if (string.IsNullOrWhiteSpace(requestingUserName)) // Double-check after parsing attempt
            {
                log.LogWarning("GetPermittedOdsForUser: Requesting user name could not be determined. Unauthorized.");
                return new UnauthorizedResult();
            }


            try
            {
                List<string> permittedOdsCodes = await GetPermittedOdsCodesAsync(requestingUserName, log);
                // Return the list of permitted ODS codes
                return new OkObjectResult(permittedOdsCodes);
            }
            catch (Exception ex)
            {
                // Use the parsed username in the error log
                log.LogError(ex, $"Error getting permitted ODS for user '{requestingUserName}'.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        [FunctionName("GetPermittedUsersForOds")]
        public static async Task<IActionResult> GetPermittedUsersForOds(
                    [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
                    ILogger log)
        {
            log.LogInformation("C# HTTP trigger function 'GetPermittedUsersForOds' processed a request.");
            string userIdFromHeader = req.Headers.ContainsKey("X-Authenticated-User-Id") ? req.Headers["X-Authenticated-User-Id"].FirstOrDefault() : null;
            string targetOds = req.Query["targetOds"];
            string requestingUserName = null; // Variable to store the parsed UserName

            if (string.IsNullOrWhiteSpace(userIdFromHeader))
            {
                log.LogWarning("GetPermittedUsersForOds: Requesting user ID not found. Unauthorized.");
                return new UnauthorizedResult();
            }
            if (string.IsNullOrWhiteSpace(targetOds))
            {
                log.LogWarning("GetPermittedUsersForOds: Missing 'targetOds' query parameter.");
                return new BadRequestObjectResult("Missing required query parameter: targetOds");
            }

            int separatorIndex = userIdFromHeader.IndexOf('-'); // Use IndexOf
            if (separatorIndex > 0)
            {
                requestingUserName = userIdFromHeader.Substring(0, separatorIndex);
                log.LogInformation($"GetPermittedUsersForOds: Parsed UserName '{requestingUserName}' from header '{userIdFromHeader}' by splitting at the first hyphen.");
            }
            else
            {
                requestingUserName = userIdFromHeader;
                log.LogWarning($"GetPermittedUsersForOds: Could not find a hyphen to parse UserName from header '{userIdFromHeader}'. Using full value as UserName for lookup.");
            }

            if (string.IsNullOrWhiteSpace(requestingUserName)) // Double-check after parsing attempt
            {
                log.LogWarning("GetPermittedUsersForOds: Requesting user name could not be determined. Unauthorized.");
                return new UnauthorizedResult();
            }

            // --- Permission Check for the specific target ODS ---
            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString)) { log.LogError("Connection string missing"); return new StatusCodeResult(StatusCodes.Status500InternalServerError); }

            var accessTable = new UserReportAccessTables(connectionString, log);
            var specificPermission = await accessTable.GetPermissionAsync(requestingUserName, targetOds);
            bool hasSpecificPermission = specificPermission != null && (specificPermission.PermissionLevel == "Viewer" || specificPermission.PermissionLevel == "Admin");

            if (!hasSpecificPermission)
            {
                // Use parsed username in log
                log.LogWarning($"GetPermittedUsersForOds: User '{requestingUserName}' does not have permission for requested ODS '{targetOds}'. Forbidden.");
                return new ForbidResult();
            }

            // --- Get Users for the Permitted ODS ---
            try
            {
                // GetFilteredUsersAsync likely uses ODS code correctly already.
                var usersInOds = await GetFilteredUsersAsync(targetOds, connectionString); // Pass connection string

                var userNames = usersInOds
                                .Select(u => u.UserName) // Extract UserName
                                .Where(un => !string.IsNullOrWhiteSpace(un))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .OrderBy(un => un)
                                .ToList();

                log.LogInformation($"Returning {userNames.Count} users permitted for user '{requestingUserName}' in ODS '{targetOds}'.");
                return new OkObjectResult(userNames);
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Error getting users for ODS '{targetOds}'.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
        // --- End Phase 2 Functions ---

        private static string DeterminePreviousState(string currentState, string context)
        {
            // Default to a sensible fallback if we can't determine
            string defaultPreviousState = "OFFLINE";

            if (string.IsNullOrEmpty(currentState))
                return defaultPreviousState;

            // Simple state transition logic
            switch (currentState.ToUpperInvariant())
            {
                case "ACTIVE":
                    // If transitioning to Active, previous was likely Away or Offline
                    if (context?.Contains("detected", StringComparison.OrdinalIgnoreCase) == true)
                        return "AWAY"; // Activity detected = was Away
                    if (context?.Contains("Available", StringComparison.OrdinalIgnoreCase) == true)
                        return "OFFLINE"; // Network Available = was Offline
                    if (context?.Contains("Start", StringComparison.OrdinalIgnoreCase) == true)
                        return "OFFLINE"; // App Start = was Offline
                    if (context?.Contains("Returned", StringComparison.OrdinalIgnoreCase) == true)
                        return "AWAY"; // User returned = was Away

                    // Default for Active if context doesn't help
                    return "AWAY";

                case "AWAY":
                    // If transitioning to Away, previous was likely Active
                    if (context?.Contains("Locked", StringComparison.OrdinalIgnoreCase) == true)
                        return "ACTIVE"; // Machine Locked = was Active
                    if (context?.Contains("Idle", StringComparison.OrdinalIgnoreCase) == true)
                        return "ACTIVE"; // Idle = was Active

                    // Default for Away if context doesn't help
                    return "ACTIVE";

                case "OFFLINE":
                    // If transitioning to Offline, previous was Active or Away
                    if (context?.Contains("Network", StringComparison.OrdinalIgnoreCase) == true)
                        return "ACTIVE"; // Network Unavailable = was Active
                    if (context?.Contains("Exit", StringComparison.OrdinalIgnoreCase) == true ||
                        context?.Contains("Closing", StringComparison.OrdinalIgnoreCase) == true)
                        return "ACTIVE"; // App Exit/Closing = was Active

                    // Default for Offline if context doesn't help
                    return "ACTIVE";

                default:
                    return defaultPreviousState;
            }
        }

        public class ReportPermission
        {
            public string AccessorUserId { get; set; }
            public string TargetOds { get; set; }
            public string TargetPracticeName { get; set; }
            public string PermissionLevel { get; set; }
            public bool Remove { get; set; } = false;
        }

        [FunctionName("QueryUserPermissions")]
        public static async Task<IActionResult> QueryUserPermissions(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "permissions/query")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function 'QueryUserPermissions' processed a request.");

            // 1. Get userId from query parameters
            string userId = req.Query["userId"];
            if (string.IsNullOrWhiteSpace(userId))
            {
                log.LogWarning("QueryUserPermissions: Missing 'userId' query parameter.");
                return new BadRequestObjectResult("Missing required query parameter: userId");
            }
            log.LogInformation($"QueryUserPermissions: Querying permissions for userId='{userId}'.");

            // 2. Instantiate Table Client
            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogError("QueryUserPermissions: Azure Storage Connection String is not configured.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            var accessTable = new UserReportAccessTables(connectionString, log);
            try
            {
                await accessTable.CreateTableAsync(); // Ensure table exists
            }
            catch (Exception ex)
            {
                log.LogError(ex, "QueryUserPermissions: Failed to ensure UserReportAccess table exists.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // 3. Query Table using the helper method
            List<UserReportAccessEntity> permissionEntities;
            try
            {
                permissionEntities = await accessTable.GetPermissionsForUserAsync(userId);
                log.LogInformation($"QueryUserPermissions: Found {permissionEntities?.Count ?? 0} permission entities for userId='{userId}'.");
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"QueryUserPermissions: Error retrieving permissions for userId='{userId}'.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // 4. Convert Entities to Client Models
            // The client expects List<ReportPermission>, so we map the entities
            var permissionModels = permissionEntities?.Select(entity => new ReportPermission
            {
                AccessorUserId = entity.AccessorUserID, // Map from entity property
                TargetOds = entity.TargetODS,          // Map from entity property (which is RowKey)
                PermissionLevel = entity.PermissionLevel
                // Note: 'Remove' flag isn't relevant when querying, defaults to false in model
            }).ToList() ?? new List<ReportPermission>(); // Handle null case

            // 5. Return Response
            // Return HTTP 200 OK with the list (even if empty), matching client expectation
            return new OkObjectResult(permissionModels);
        }

        [FunctionName("ManageReportPermission")]
        public static async Task<IActionResult> ManageReportPermission(
                   [HttpTrigger(AuthorizationLevel.Function, "post", Route = "permissions/manage")] HttpRequest req,
                   ILogger log)
        {
            log.LogInformation("C# HTTP trigger function 'ManageReportPermission' (Batch Enabled) processed a request.");

            // 1. Read and Deserialize LIST from Request Body
            string requestBody;
            try
            {
                requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    log.LogWarning("ManageReportPermission (Batch): Received empty request body.");
                    return new BadRequestObjectResult("Request body cannot be empty. Expecting a JSON array of permissions.");
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "ManageReportPermission (Batch): Error reading request body.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            List<ReportPermission> permissionPayloads; // Expecting a LIST now
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                permissionPayloads = JsonSerializer.Deserialize<List<ReportPermission>>(requestBody, options); // Deserialize LIST

                if (permissionPayloads == null || !permissionPayloads.Any()) // Check if list is null or empty
                {
                    log.LogError("ManageReportPermission (Batch): Failed to deserialize or received empty permission list.");
                    return new BadRequestObjectResult("Invalid request payload format or empty list.");
                }

                log.LogInformation($"ManageReportPermission (Batch): Received {permissionPayloads.Count} permission operations.");

                // Optional: Validate individual items within the list here if needed
                foreach (var payload in permissionPayloads)
                {
                    if (string.IsNullOrWhiteSpace(payload.AccessorUserId) || string.IsNullOrWhiteSpace(payload.TargetOds) || (!payload.Remove && string.IsNullOrWhiteSpace(payload.PermissionLevel)))
                    {
                        log.LogWarning($"ManageReportPermission (Batch): Invalid item found in list: User='{payload.AccessorUserId}', ODS='{payload.TargetOds}', Level='{payload.PermissionLevel}', Remove='{payload.Remove}'.");
                        return new BadRequestObjectResult("One or more permission items in the list are invalid.");
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                log.LogError(jsonEx, "ManageReportPermission (Batch): Error deserializing JSON list.");
                return new BadRequestObjectResult($"Invalid JSON format: {jsonEx.Message}");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "ManageReportPermission (Batch): Unexpected error during deserialization or validation.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // 2. Instantiate Table Client
            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogError("ManageReportPermission (Batch): Azure Storage Connection String is not configured.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            var accessTable = new UserReportAccessTables(connectionString, log);
            try
            {
                await accessTable.CreateTableAsync(); // Ensure table exists
            }
            catch (Exception ex)
            {
                log.LogError(ex, "ManageReportPermission (Batch): Failed to ensure UserReportAccess table exists.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // 3. Prepare Batch Operation
            var transactionActions = new List<TableTransactionAction>();
            foreach (var payload in permissionPayloads)
            {
                if (payload.Remove)
                {
                    // Use new ETag("*") for unconditional delete within a transaction
                    transactionActions.Add(new TableTransactionAction(TableTransactionActionType.Delete, new UserReportAccessEntity { PartitionKey = payload.AccessorUserId, RowKey = payload.TargetOds, ETag = new ETag("*") }));
                    log.LogInformation($"ManageReportPermission (Batch): Prepared DELETE action for User='{payload.AccessorUserId}', ODS='{payload.TargetOds}'.");
                }
                else
                {
                    // Add Upsert action (Replace mode)
                    var entity = new UserReportAccessEntity
                    {
                        PartitionKey = payload.AccessorUserId,
                        RowKey = payload.TargetOds, // Should be True ODS
                        AccessorUserID = payload.AccessorUserId,
                        TargetODS = payload.TargetOds, // Should be True ODS
                        PermissionLevel = payload.PermissionLevel,
                        Timestamp = DateTimeOffset.UtcNow,
                        // Use new ETag("*") to signify replace regardless of current state
                        ETag = new ETag("*")
                    };
                    transactionActions.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace, entity));
                    log.LogInformation($"ManageReportPermission (Batch): Prepared UPSERT action for User='{payload.AccessorUserId}', ODS='{payload.TargetOds}', Level='{payload.PermissionLevel}'.");
                }
            }

            // 4. Execute Batch Operation
            bool overallSuccess = true;
            List<Response<IReadOnlyList<Response>>> batchResponses = null;
            try
            {
                if (transactionActions.Any())
                {
                    log.LogInformation($"ManageReportPermission (Batch): Submitting {transactionActions.Count} actions in batches...");
                    // Submit actions in chunks using the helper method
                    batchResponses = await accessTable.SubmitTransactionInChunksAsync(transactionActions, log);

                    // Optional: Check individual batch responses for failures if needed
                    if (batchResponses == null || batchResponses.Any(r => r == null || r.GetRawResponse().IsError))
                    {
                        log.LogError("ManageReportPermission (Batch): One or more batch submissions failed.");
                        overallSuccess = false;
                    }
                    else
                    {
                        log.LogInformation($"ManageReportPermission (Batch): All batch submissions completed.");
                    }
                }
                else
                {
                    log.LogWarning("ManageReportPermission (Batch): No valid actions prepared.");
                    // Consider if this case should be an error or success (e.g., success if input list was empty)
                    // For now, treat as success as no operation needed/failed.
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "ManageReportPermission (Batch): Error submitting batch transaction.");
                overallSuccess = false;
            }

            // 5. Return Response
            if (overallSuccess)
            {
                log.LogInformation($"ManageReportPermission (Batch): Successfully processed {permissionPayloads.Count} permission operations.");
                return new OkObjectResult($"Successfully processed {permissionPayloads.Count} permission operations.");
            }
            else
            {
                log.LogError($"ManageReportPermission (Batch): Failed to process one or more permission operations.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError); // Indicate error
            }
        }

        /// <summary>
        /// Azure Function to Set/Update a specific feature setting.
        /// Expects JSON body: { "featureName": "...", "scopeIdentifier": "...", "isEnabled": true/false }
        /// </summary>
        [FunctionName("SetFeatureSetting")]
        public static async Task<IActionResult> SetFeatureSetting(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "featuresettings/set")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function 'SetFeatureSetting' processed a request.");
            // TODO: Add Admin Authorization Check here

            string requestBody;
            try
            {
                requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    return new BadRequestObjectResult("Request body cannot be empty.");
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error reading request body for SetFeatureSetting.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            string featureName, scopeIdentifier;
            bool isEnabled;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(requestBody))
                {
                    if (!document.RootElement.TryGetProperty("featureName", out JsonElement fnElement) || fnElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(fnElement.GetString()))
                    {
                        return new BadRequestObjectResult("Missing or invalid 'featureName' property in request body.");
                    }
                    if (!document.RootElement.TryGetProperty("scopeIdentifier", out JsonElement siElement) || siElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(siElement.GetString()))
                    {
                        return new BadRequestObjectResult("Missing or invalid 'scopeIdentifier' property in request body.");
                    }
                    if (!document.RootElement.TryGetProperty("isEnabled", out JsonElement ieElement) || (ieElement.ValueKind != JsonValueKind.True && ieElement.ValueKind != JsonValueKind.False))
                    {
                        return new BadRequestObjectResult("Missing or invalid 'isEnabled' property (must be true or false) in request body.");
                    }

                    featureName = fnElement.GetString();
                    scopeIdentifier = siElement.GetString();
                    isEnabled = ieElement.GetBoolean();

                    // Basic validation for scope prefixes
                    if (!scopeIdentifier.StartsWith("GLOBAL:") && !scopeIdentifier.StartsWith("ODS_") && !scopeIdentifier.StartsWith("USER_"))
                    {
                        return new BadRequestObjectResult("Invalid 'scopeIdentifier' format. Must start with GLOBAL:, ODS_, or USER_.");
                    }
                    if (scopeIdentifier.StartsWith("USER_") && scopeIdentifier.Substring(5) != scopeIdentifier.Substring(5).ToUpperInvariant())
                    {
                        // Optional: Enforce uppercase for username part immediately, or handle in service
                        log.LogWarning($"SetFeatureSetting: Username part of USER scope '{scopeIdentifier}' is not uppercase. Proceeding, but ensure consistency.");
                    }
                }
            }
            catch (JsonException jsonEx)
            {
                log.LogError(jsonEx, "Error deserializing request body JSON for SetFeatureSetting.");
                return new BadRequestObjectResult($"Invalid JSON format: {jsonEx.Message}");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Unexpected error processing request body for SetFeatureSetting.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogError("SetFeatureSetting: Azure Storage Connection String not configured.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            var settingsTable = new FeatureSettingsTable(connectionString, log);
            await settingsTable.CreateTableAsync(); // Ensure table exists

            bool success = await settingsTable.SetSettingAsync(featureName, scopeIdentifier, isEnabled);
            if (success)
            {
                return new OkObjectResult($"Setting '{scopeIdentifier}' for feature '{featureName}' set to {isEnabled}.");
            }
            else
            {
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Azure Function to Delete a specific feature setting.
        /// Expects JSON body: { "featureName": "...", "scopeIdentifier": "..." }
        /// </summary>
        [FunctionName("DeleteFeatureSetting")]
        public static async Task<IActionResult> DeleteFeatureSetting(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "featuresettings/delete")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function 'DeleteFeatureSetting' processed a request.");

            // TODO: Add Admin Authorization Check here

            string requestBody;
            try
            {
                requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    return new BadRequestObjectResult("Request body cannot be empty.");
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error reading request body for DeleteFeatureSetting.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            string featureName, scopeIdentifier;
            try
            {
                using (JsonDocument document = JsonDocument.Parse(requestBody))
                {
                    if (!document.RootElement.TryGetProperty("featureName", out JsonElement fnElement) || fnElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(fnElement.GetString()))
                    {
                        return new BadRequestObjectResult("Missing or invalid 'featureName' property in request body.");
                    }
                    if (!document.RootElement.TryGetProperty("scopeIdentifier", out JsonElement siElement) || siElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(siElement.GetString()))
                    {
                        return new BadRequestObjectResult("Missing or invalid 'scopeIdentifier' property in request body.");
                    }
                    featureName = fnElement.GetString();
                    scopeIdentifier = siElement.GetString();
                }
            }
            catch (JsonException jsonEx)
            {
                log.LogError(jsonEx, "Error deserializing request body JSON for DeleteFeatureSetting.");
                return new BadRequestObjectResult($"Invalid JSON format: {jsonEx.Message}");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Unexpected error processing request body for DeleteFeatureSetting.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }


            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogError("DeleteFeatureSetting: Azure Storage Connection String not configured.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            var settingsTable = new FeatureSettingsTable(connectionString, log);
            await settingsTable.CreateTableAsync(); // Ensure table exists (though unlikely needed for delete)

            bool success = await settingsTable.DeleteSettingAsync(featureName, scopeIdentifier);
            if (success)
            {
                // Status 200 OK if deleted, or 204 No Content if it didn't exist but operation is considered successful.
                // OK is fine here.
                return new OkObjectResult($"Setting '{scopeIdentifier}' for feature '{featureName}' deleted (if it existed).");
            }
            else
            {
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Azure Function to Get a specific feature setting.
        /// Uses query parameters: ?featureName=...&scopeIdentifier=...
        /// </summary>
        [FunctionName("GetFeatureSetting")]
        public static async Task<IActionResult> GetFeatureSetting(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "featuresettings/get")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function 'GetFeatureSetting' processed a request.");

            // TODO: Add Admin Authorization Check here (or broader if client might call this directly)

            string featureName = req.Query["featureName"];
            string scopeIdentifier = req.Query["scopeIdentifier"];

            if (string.IsNullOrWhiteSpace(featureName) || string.IsNullOrWhiteSpace(scopeIdentifier))
            {
                return new BadRequestObjectResult("Missing required query parameters: 'featureName' and 'scopeIdentifier'.");
            }

            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogError("GetFeatureSetting: Azure Storage Connection String not configured.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            var settingsTable = new FeatureSettingsTable(connectionString, log);
            // No need to CreateTable here, GetEntityIfExistsAsync handles non-existent table gracefully

            FeatureSettingsEntity setting = await settingsTable.GetSettingAsync(featureName, scopeIdentifier);

            if (setting != null)
            {
                // Return the found setting
                return new OkObjectResult(setting);
            }
            else
            {
                // Return 404 Not Found if the specific setting doesn't exist
                return new NotFoundObjectResult($"Setting for Feature='{featureName}', Scope='{scopeIdentifier}' not found.");
            }
        }

        /// <summary>
        /// Azure Function to Get the EFFECTIVE feature setting for a user/ODS,
        /// implementing the User > ODS > Global hierarchy.
        /// Uses query parameters: ?featureName=...&userName=...&odsCode=...
        /// </summary>
        [FunctionName("GetEffectiveFeatureSetting")]
        public static async Task<IActionResult> GetEffectiveFeatureSetting(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "featuresettings/effective")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function 'GetEffectiveFeatureSetting' processed a request.");
            // No specific admin auth needed here typically, as clients need to call this.
            // Rely on Function Key security.
            string featureName = req.Query["featureName"];
            string userName = req.Query["userName"]; // Case might matter depending on storage/comparison
            string odsCode = req.Query["odsCode"];
            // Case might matter

            if (string.IsNullOrWhiteSpace(featureName) || string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(odsCode))
            {
                return new BadRequestObjectResult("Missing required query parameters: 'featureName', 'userName', and 'odsCode'.");
            }

            // --- Normalize User Input ---
            string normalizedUserName = userName.ToUpperInvariant();
            // Ensure uppercase for lookup
            // ODS Code case sensitivity depends on how it's stored/compared, assuming case-insensitive for now.
            log.LogInformation($"Calculating effective status for Feature='{featureName}', User='{normalizedUserName}', ODS='{odsCode}'.");

            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogError("GetEffectiveFeatureSetting: Azure Storage Connection String not configured.");
                // Default to disabled in case of server config error
                return new OkObjectResult(new { effectiveIsEnabled = false });
            }

            var settingsTable = new FeatureSettingsTable(connectionString, log);
            // No need to CreateTable here for reads

            bool effectiveIsEnabled = false;
            // Default to disabled

            try
            {
                // 1. Check User Setting
                string userScope = $"USER_{normalizedUserName}";
                FeatureSettingsEntity userSetting = await settingsTable.GetSettingAsync(featureName, userScope);
                if (userSetting != null)
                {
                    effectiveIsEnabled = userSetting.IsEnabled;
                    log.LogInformation($"Effective Status determined by User setting ({userScope}): {effectiveIsEnabled}");
                }
                else
                {
                    // 2. Check ODS Setting (if User setting not found)
                    string odsScope = $"ODS_{odsCode}";
                    // Use potentially case-sensitive odsCode for RowKey
                    FeatureSettingsEntity odsSetting = await settingsTable.GetSettingAsync(featureName, odsScope);
                    if (odsSetting != null)
                    {
                        effectiveIsEnabled = odsSetting.IsEnabled;
                        log.LogInformation($"Effective Status determined by ODS setting ({odsScope}): {effectiveIsEnabled}");
                    }
                    else
                    {
                        // 3. Check Global Setting (if User and ODS settings not found)
                        string globalScope = "GLOBAL:";
                        FeatureSettingsEntity globalSetting = await settingsTable.GetSettingAsync(featureName, globalScope);
                        if (globalSetting != null)
                        {
                            effectiveIsEnabled = globalSetting.IsEnabled;
                            log.LogInformation($"Effective Status determined by Global setting ({globalScope}): {effectiveIsEnabled}");
                        }
                        else
                        {
                            // 4. Default (if no settings found at any level)
                            effectiveIsEnabled = false;
                            // Requirement FR-BE-04
                            log.LogInformation($"No specific settings found. Defaulting to disabled for Feature='{featureName}'.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Error during effective setting lookup for Feature='{featureName}', User='{normalizedUserName}', ODS='{odsCode}'. Defaulting to disabled.");
                effectiveIsEnabled = false; // Default to disabled on error
                                            // Consider returning a 500 error instead if appropriate
                                            // return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // Return the calculated effective status
            return new OkObjectResult(new { effectiveIsEnabled = effectiveIsEnabled });
        }


        /// <summary>
        /// Azure Function to Get all settings, optionally filtered by a specific feature.
        /// Uses optional query parameter: ?featureName=...
        /// If featureName is omitted, returns settings for all features.
        /// </summary>
        [FunctionName("GetAllFeatureSettings")]
        public static async Task<IActionResult> GetAllFeatureSettings(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "featuresettings/getall")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function 'GetAllFeatureSettings' processed a request.");
            // TODO: Add Admin Authorization Check here if accessing sensitive features

            // Make featureName OPTIONAL
            string featureName = req.Query["featureName"];
            // Still read it if provided

            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogError("GetAllFeatureSettings: Azure Storage Connection String not configured.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            var settingsTable = new FeatureSettingsTable(connectionString, log);
            List<FeatureSettingsEntity> settings;

            if (!string.IsNullOrWhiteSpace(featureName))
            {
                // If featureName IS provided, use the existing method to get settings for that specific feature
                log.LogInformation($"GetAllFeatureSettings: Filtering by featureName='{featureName}'.");
                settings = await settingsTable.GetAllSettingsForFeatureAsync(featureName);
            }
            else
            {
                // If featureName is NOT provided, get ALL settings (needs a new method in FeatureSettingsTable)
                log.LogInformation($"GetAllFeatureSettings: No featureName provided, retrieving all settings.");
                // *** We need a new method in FeatureSettingsTable to get ALL entities ***
                // Let's add that method definition below and call it here.
                settings = await settingsTable.GetAllSettingsAsync(); // NEW method call
            }

            // Return the list (even if empty)
            return new OkObjectResult(settings);
        }

        /// <summary>
        /// Azure Function to Delete ALL ODS-specific settings for a given feature,
        /// causing them to inherit from Global.
        /// Expects JSON body: { "featureName": "..." }
        /// </summary>
        [FunctionName("DeleteAllOdsOverrides")]
        public static async Task<IActionResult> DeleteAllOdsOverrides(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "featuresettings/deletebulkods")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function 'DeleteAllOdsOverrides' processed a request.");
            // TODO: Add Admin Auth Check

            // 1. Deserialize Request Body
            string featureName;
            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(requestBody)) return new BadRequestObjectResult("Request body cannot be empty.");
                using (JsonDocument document = JsonDocument.Parse(requestBody))
                {
                    if (!document.RootElement.TryGetProperty("featureName", out JsonElement fnElement) || fnElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(fnElement.GetString()))
                        return new BadRequestObjectResult("Missing or invalid 'featureName' property.");
                    featureName = fnElement.GetString();
                }
                log.LogInformation($"DeleteAllOdsOverrides: Request to delete all ODS overrides for Feature='{featureName}'.");
            }
            catch (Exception ex) { log.LogError(ex, "Error deserializing request body for DeleteAllOdsOverrides."); return new BadRequestObjectResult("Invalid request body."); }


            // 2. Connect to Table Storage
            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString)) { log.LogError("DeleteAllOdsOverrides: Connection String missing."); return new StatusCodeResult(StatusCodes.Status500InternalServerError); }
            var settingsTable = new FeatureSettingsTable(connectionString, log);

            // 3. Query for *EXISTING* ODS settings for the feature
            List<FeatureSettingsEntity> settingsToDelete;
            try
            {
                log.LogInformation($"DeleteAllOdsOverrides: Querying existing ODS settings for Feature='{featureName}'.");
                string pkFilter = TableClient.CreateQueryFilter($"PartitionKey eq {featureName}");
                string rkFilter = $"RowKey ge 'ODS_' and RowKey lt 'ODT_'";
                string filter = $"{pkFilter} and {rkFilter}";
                settingsToDelete = new List<FeatureSettingsEntity>();
                // Use the public QuerySettingsAsync method
                await foreach (var entity in settingsTable.QuerySettingsAsync(filter))
                {
                    settingsToDelete.Add(entity);
                }
                log.LogInformation($"DeleteAllOdsOverrides: Found {settingsToDelete.Count} existing ODS settings to delete for Feature='{featureName}'.");
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"DeleteAllOdsOverrides: Error querying settings to delete for Feature='{featureName}'.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // 4. Prepare Batch Delete Actions *ONLY* for found entities
            var transactionActions = new List<TableTransactionAction>();
            foreach (var setting in settingsToDelete) // Iterate only the ones found
            {
                transactionActions.Add(new TableTransactionAction(TableTransactionActionType.Delete, new TableEntity(setting.PartitionKey, setting.RowKey) { ETag = ETag.All }));
            }

            // 5. Execute Batch Operation (if any entities were found)
            try
            {
                if (transactionActions.Any())
                {
                    log.LogInformation($"DeleteAllOdsOverrides: Submitting {transactionActions.Count} DELETE actions for Feature='{featureName}'.");
                    await settingsTable.SubmitTransactionInChunksAsync(transactionActions, log); // Helper handles chunks
                    log.LogInformation($"DeleteAllOdsOverrides: Batch DELETE completed for Feature='{featureName}'.");
                }
                else
                {
                    log.LogInformation($"DeleteAllOdsOverrides: No existing ODS overrides found to delete for Feature='{featureName}'.");
                }
                // Return success whether deletions happened or not (goal is overrides are gone)
                return new OkObjectResult($"Bulk ODS override deletion for feature '{featureName}' processed. Found and deleted {transactionActions.Count} entries.");
            }
            catch (Exception ex) // Catch errors from SubmitTransactionInChunksAsync (which now re-throws)
            {
                log.LogError(ex, $"DeleteAllOdsOverrides: Error during bulk delete batch submission for Feature='{featureName}'.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Azure Function to Delete ALL USER-specific settings for a given feature,
        /// causing them to inherit from ODS or Global.
        /// Fetches the user list from the 'Users' table.
        /// Expects JSON body: { "featureName": "..." }
        /// </summary>
        [FunctionName("DeleteAllUserOverrides")]
        public static async Task<IActionResult> DeleteAllUserOverrides(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "featuresettings/deletebulkusers")] HttpRequest req,
            ILogger log) // Logger is passed by the runtime
        {
            log.LogInformation("C# HTTP trigger function 'DeleteAllUserOverrides' processed a request.");
            // TODO: Add appropriate Admin Authorization Check here

            // 1. Deserialize Request Body (Just Feature Name needed)
            string featureName;
            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(requestBody)) return new BadRequestObjectResult("Request body cannot be empty.");
                using (JsonDocument document = JsonDocument.Parse(requestBody))
                {
                    if (!document.RootElement.TryGetProperty("featureName", out JsonElement fnElement) || fnElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(fnElement.GetString()))
                        return new BadRequestObjectResult("Missing or invalid 'featureName' property.");
                    featureName = fnElement.GetString();
                }
                log.LogInformation($"DeleteAllUserOverrides: Request to delete all User overrides for Feature='{featureName}'.");
            }
            catch (Exception ex) { log.LogError(ex, "Error deserializing request body for DeleteAllUserOverrides."); return new BadRequestObjectResult("Invalid request body."); }


            // 2. Connect to Table Storage
            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString)) { log.LogError("DeleteAllUserOverrides: Connection String missing."); return new StatusCodeResult(StatusCodes.Status500InternalServerError); }
            var settingsTable = new FeatureSettingsTable(connectionString, log);

            // 3. Query for *EXISTING* USER settings for the feature
            List<FeatureSettingsEntity> settingsToDelete;
            try
            {
                log.LogInformation($"DeleteAllUserOverrides: Querying existing USER settings for Feature='{featureName}'.");
                string pkFilter = TableClient.CreateQueryFilter($"PartitionKey eq {featureName}");
                // Filter for RowKeys starting with USER_
                string rkFilter = $"RowKey ge 'USER_' and RowKey lt 'USER`'"; // USER_ comes before USER` lexicographically
                string filter = $"{pkFilter} and {rkFilter}";

                settingsToDelete = new List<FeatureSettingsEntity>();
                // Use the public QuerySettingsAsync method
                await foreach (var entity in settingsTable.QuerySettingsAsync(filter))
                {
                    settingsToDelete.Add(entity);
                }
                log.LogInformation($"DeleteAllUserOverrides: Found {settingsToDelete.Count} existing USER settings to delete for Feature='{featureName}'.");
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"DeleteAllUserOverrides: Error querying settings to delete for Feature='{featureName}'.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // 4. Prepare Batch Delete Actions *ONLY* for found entities
            var transactionActions = new List<TableTransactionAction>();
            foreach (var setting in settingsToDelete) // Iterate only the ones found
            {
                transactionActions.Add(new TableTransactionAction(TableTransactionActionType.Delete, new TableEntity(setting.PartitionKey, setting.RowKey) { ETag = ETag.All }));
            }

            // 5. Execute Batch Operation (if any entities were found)
            try
            {
                if (transactionActions.Any())
                {
                    log.LogInformation($"DeleteAllUserOverrides: Submitting {transactionActions.Count} DELETE actions for Feature='{featureName}'.");
                    await settingsTable.SubmitTransactionInChunksAsync(transactionActions, log); // Helper handles chunks
                    log.LogInformation($"DeleteAllUserOverrides: Batch DELETE completed for Feature='{featureName}'.");
                }
                else
                {
                    log.LogInformation($"DeleteAllUserOverrides: No existing USER overrides found to delete for Feature='{featureName}'.");
                }
                // Return success whether deletions happened or not
                return new OkObjectResult($"Bulk User override deletion for feature '{featureName}' processed. Found and deleted {transactionActions.Count} entries.");
            }
            catch (Exception ex) // Catch errors from SubmitTransactionInChunksAsync (which now re-throws)
            {
                log.LogError(ex, $"DeleteAllUserOverrides: Error during bulk delete batch submission for Feature='{featureName}'.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Azure Function to Set/Update a feature setting for ALL ODS scopes simultaneously
        /// by fetching the master ODS list from the edgealert2.json blob.
        /// Expects JSON body: { "featureName": "...", "isEnabled": true/false }
        /// </summary>
        [FunctionName("SetBulkOdsSettings")]
        public static async Task<IActionResult> SetBulkOdsSettings(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "featuresettings/setbulkods")] HttpRequest req,
            ILogger log) // Logger is passed by the runtime
        {
            log.LogInformation("C# HTTP trigger function 'SetBulkOdsSettings' processed a request.");
            // TODO: Add appropriate Admin Authorization Check here

            // 1. Deserialize Request Body (Feature Name and Target State)
            string requestBody;
            string featureName;
            bool isEnabled;
            try
            {
                requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    return new BadRequestObjectResult("Request body cannot be empty.");
                }

                using (JsonDocument document = JsonDocument.Parse(requestBody))
                {
                    if (!document.RootElement.TryGetProperty("featureName", out JsonElement fnElement) || fnElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(fnElement.GetString()))
                    {
                        return new BadRequestObjectResult("Missing or invalid 'featureName' property in request body.");
                    }
                    if (!document.RootElement.TryGetProperty("isEnabled", out JsonElement ieElement) || (ieElement.ValueKind != JsonValueKind.True && ieElement.ValueKind != JsonValueKind.False))
                    {
                        return new BadRequestObjectResult("Missing or invalid 'isEnabled' property (must be true or false) in request body.");
                    }
                    featureName = fnElement.GetString();
                    isEnabled = ieElement.GetBoolean();
                }
                log.LogInformation($"SetBulkOdsSettings: Request to set Feature='{featureName}' to State='{isEnabled}' for all ODS.");
            }
            catch (JsonException jsonEx)
            {
                log.LogError(jsonEx, "Error deserializing request body JSON for SetBulkOdsSettings.");
                return new BadRequestObjectResult($"Invalid JSON format: {jsonEx.Message}");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Unexpected error processing request body for SetBulkOdsSettings.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // 2. Fetch ODS List from JSON Blob
            List<string> allOdsCodes;
            try
            {
                // Call the static helper method defined previously
                allOdsCodes = await GetOdsListFromJsonBlobAsync(log);
                if (allOdsCodes == null || !allOdsCodes.Any())
                {
                    log.LogError("SetBulkOdsSettings: Failed to retrieve a valid list of ODS codes from JSON blob.");
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
                log.LogInformation($"SetBulkOdsSettings: Successfully retrieved {allOdsCodes.Count} distinct ODS codes from JSON blob.");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "SetBulkOdsSettings: Error occurred while fetching or parsing ODS list from JSON blob.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // 3. Connect to Table Storage
            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogError("SetBulkOdsSettings: Azure Storage Connection String not configured.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // Assuming FeatureSettingsTable is defined elsewhere in the same namespace or accessible
            var settingsTable = new FeatureSettingsTable(connectionString, log);
            await settingsTable.CreateTableAsync(); // Ensure table exists

            // 4. Prepare Batch Upsert Actions
            var transactionActions = new List<TableTransactionAction>();
            foreach (var odsCode in allOdsCodes)
            {
                if (string.IsNullOrWhiteSpace(odsCode)) continue; // Skip empty ODS codes

                string scopeIdentifier = $"ODS_{odsCode}"; // Construct RowKey
                var entity = new FeatureSettingsEntity
                {
                    PartitionKey = featureName,
                    RowKey = scopeIdentifier,
                    IsEnabled = isEnabled,
                    Timestamp = DateTimeOffset.UtcNow,
                    ETag = ETag.All // Use wildcard ETag for UpsertReplace
                };
                transactionActions.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace, entity));
            }

            // 5. Execute Batch Operation
            try
            {
                if (transactionActions.Any())
                {
                    log.LogInformation($"SetBulkOdsSettings: Submitting {transactionActions.Count} UpsertReplace actions for Feature='{featureName}'.");
                    await settingsTable.SubmitTransactionInChunksAsync(transactionActions, log);
                    log.LogInformation($"SetBulkOdsSettings: Batch UpsertReplace completed for Feature='{featureName}'.");
                }
                else
                {
                    log.LogWarning($"SetBulkOdsSettings: No valid ODS codes found in the JSON blob, no actions submitted.");
                }

                return new OkObjectResult($"Bulk ODS update for feature '{featureName}' processed. Attempted to set state for {transactionActions.Count} ODS codes to {isEnabled}.");

            }
            catch (RequestFailedException rfEx)
            {
                log.LogError(rfEx, $"SetBulkOdsSettings: Azure Table Storage error during bulk update for Feature='{featureName}'. Status: {rfEx.Status}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"SetBulkOdsSettings: Unexpected error during bulk update for Feature='{featureName}'.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Azure Function to Set/Update a feature setting for ALL known USER scopes simultaneously.
        /// Fetches the user list from the 'Users' table.
        /// Expects JSON body: { "featureName": "...", "isEnabled": true/false }
        /// </summary>
        [FunctionName("SetBulkUserSettings")]
        public static async Task<IActionResult> SetBulkUserSettings(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "featuresettings/setbulkusers")] HttpRequest req,
            ILogger log) // Logger is passed by the runtime
        {
            log.LogInformation("C# HTTP trigger function 'SetBulkUserSettings' processed a request.");
            // TODO: Add appropriate Admin Authorization Check here

            // 1. Deserialize Request Body (Feature Name and Target State)
            string requestBody;
            string featureName;
            bool isEnabled;
            try
            {
                requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    return new BadRequestObjectResult("Request body cannot be empty.");
                }

                using (JsonDocument document = JsonDocument.Parse(requestBody))
                {
                    if (!document.RootElement.TryGetProperty("featureName", out JsonElement fnElement) || fnElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(fnElement.GetString()))
                    {
                        return new BadRequestObjectResult("Missing or invalid 'featureName' property in request body.");
                    }
                    if (!document.RootElement.TryGetProperty("isEnabled", out JsonElement ieElement) || (ieElement.ValueKind != JsonValueKind.True && ieElement.ValueKind != JsonValueKind.False))
                    {
                        return new BadRequestObjectResult("Missing or invalid 'isEnabled' property (must be true or false) in request body.");
                    }
                    featureName = fnElement.GetString();
                    isEnabled = ieElement.GetBoolean();
                }
                log.LogInformation($"SetBulkUserSettings: Request to set Feature='{featureName}' to State='{isEnabled}' for all Users.");
            }
            catch (JsonException jsonEx)
            {
                log.LogError(jsonEx, "Error deserializing request body JSON for SetBulkUserSettings.");
                return new BadRequestObjectResult($"Invalid JSON format: {jsonEx.Message}");
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Unexpected error processing request body for SetBulkUserSettings.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // 2. Connect to Storage & Get User List
            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogError("SetBulkUserSettings: Azure Storage Connection String not configured.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            List<string> allUserNames;
            try
            {
                // Assuming EdgeAlertTables is defined in the same namespace or accessible
                // It requires the connection string and table name ("Users")
                var userTable = new EdgeAlertTables(connectionString, "Users");
                // Fetch all user records
                var allUsers = await userTable.GetAllUserAsync(); // Method from EdgeAlertTables class

                if (allUsers == null)
                {
                    log.LogError("SetBulkUserSettings: Failed to retrieve user list from Users table (GetAllUserAsync returned null).");
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }

                // Extract distinct, non-empty, uppercase usernames
                allUserNames = allUsers
                                .Select(u => u.UserName)
                                .Where(un => !string.IsNullOrWhiteSpace(un))
                                .Select(un => un.ToUpperInvariant()) // Ensure uppercase for consistency
                                .Distinct()
                                .ToList();

                log.LogInformation($"SetBulkUserSettings: Successfully retrieved {allUserNames.Count} distinct user names from Users table.");
                if (!allUserNames.Any())
                {
                    log.LogWarning("SetBulkUserSettings: No user names found in the Users table. No settings will be applied.");
                    // Return success because there's nothing to do.
                    return new OkObjectResult($"Bulk User update for feature '{featureName}' processed. No users found to update.");
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "SetBulkUserSettings: Error occurred while fetching or processing user list from Users table.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // 3. Connect to FeatureSettings Table
            var settingsTable = new FeatureSettingsTable(connectionString, log);
            await settingsTable.CreateTableAsync(); // Ensure table exists

            // 4. Prepare Batch Upsert Actions
            var transactionActions = new List<TableTransactionAction>();
            foreach (var userName in allUserNames)
            {
                // Already uppercase from step 2
                string scopeIdentifier = $"USER_{userName}"; // Construct RowKey
                var entity = new FeatureSettingsEntity
                {
                    PartitionKey = featureName,
                    RowKey = scopeIdentifier,
                    IsEnabled = isEnabled,
                    Timestamp = DateTimeOffset.UtcNow,
                    ETag = ETag.All // Use wildcard ETag for UpsertReplace
                };
                transactionActions.Add(new TableTransactionAction(TableTransactionActionType.UpsertReplace, entity));
            }

            // 5. Execute Batch Operation
            try
            {
                if (transactionActions.Any())
                {
                    log.LogInformation($"SetBulkUserSettings: Submitting {transactionActions.Count} UpsertReplace actions for Feature='{featureName}'.");
                    // *** Assumes SubmitTransactionInChunksAsync exists in FeatureSettingsTable ***
                    await settingsTable.SubmitTransactionInChunksAsync(transactionActions, log);
                    log.LogInformation($"SetBulkUserSettings: Batch UpsertReplace completed for Feature='{featureName}'.");
                }
                // else case handled during user list retrieval

                return new OkObjectResult($"Bulk User update for feature '{featureName}' processed. Attempted to set state for {transactionActions.Count} users to {isEnabled}.");

            }
            catch (RequestFailedException rfEx)
            {
                log.LogError(rfEx, $"SetBulkUserSettings: Azure Table Storage error during bulk update for Feature='{featureName}'. Status: {rfEx.Status}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"SetBulkUserSettings: Unexpected error during bulk update for Feature='{featureName}'.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Helper Function to fetch ODS list from the JSON blob storage.
        /// Uses a static HttpClient instance (less ideal than HttpClientFactory, but avoids DI refactoring for now).
        /// </summary>
        private static HttpClient staticHttpClient = new HttpClient(); // Static instance
        private static async Task<List<string>> GetOdsListFromJsonBlobAsync(ILogger log)
        {
            string url = "https://edgebitslimiteduksouth.blob.core.windows.net/edgebitslimited/edgealert2.json";
            try
            {
                // Use the static HttpClient instance
                string jsonContent = await staticHttpClient.GetStringAsync(url);
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    log.LogError("Fetched JSON content from blob is empty.");
                    return null;
                }

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var rootObject = JsonSerializer.Deserialize<JsonRootObject>(jsonContent, options);

                if (rootObject?.Locations == null)
                {
                    log.LogError("Failed to deserialize JSON or 'Locations' array is missing/null.");
                    return null;
                }

                // Extract distinct, non-empty ODS codes
                var odsCodes = rootObject.Locations
                                   .Select(l => l.ODS)
                                   .Where(ods => !string.IsNullOrWhiteSpace(ods))
                                   .Distinct(StringComparer.OrdinalIgnoreCase) // Case-insensitive distinct
                                   .ToList();

                return odsCodes;
            }
            catch (HttpRequestException httpEx)
            {
                log.LogError(httpEx, $"HTTP error fetching ODS list from {url}.");
                return null;
            }
            catch (JsonException jsonEx)
            {
                log.LogError(jsonEx, $"Error deserializing ODS list JSON from {url}.");
                return null;
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Unexpected error fetching/parsing ODS list from {url}.");
                return null;
            }
        }
    }

    /// <summary>
    /// Represents a single feature setting entry in the FeatureSettings Azure Table.
    /// Allows controlling features at Global, ODS, or User scope.
    /// </summary>
    public class FeatureSettingsEntity : ITableEntity
    {
        /// <summary>
        /// PartitionKey: The name of the feature being controlled (e.g., "UserTimeTracking-Feature").
        /// </summary>
        public string PartitionKey { get; set; }

        /// <summary>
        /// RowKey: The scope identifier for the setting.
        /// Prefixes define the scope:
        /// - GLOBAL: For the global default setting.
        /// - ODS_{OdsCode}: For an ODS-specific setting (e.g., "ODS_K81036").
        /// - USER_{UserName}: For a user-specific setting (e.g., "USER_JSMITH", UserName should be uppercase).
        /// </summary>
        public string RowKey { get; set; }

        /// <summary>
        /// Indicates whether the feature is enabled (true) or disabled (false) for this specific scope.
        /// </summary>
        public bool IsEnabled { get; set; }

        // Required by ITableEntity
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }

    /// <summary>
    /// Provides methods to interact with the FeatureSettings Azure Table.
    /// </summary>
    public class FeatureSettingsTable
    {
        private readonly TableClient _tableClient;
        private const string TableName = "FeatureSettings";
        private readonly ILogger _log;
        public FeatureSettingsTable(string connectionString, ILogger log)
        {
            _tableClient = new TableClient(connectionString, TableName);
            _log = log;
        }

        /// <summary>
        /// Ensures the FeatureSettings table exists in Azure Storage.
        /// </summary>
        public async Task CreateTableAsync()
        {
            try
            {
                await _tableClient.CreateIfNotExistsAsync();
                _log.LogInformation($"Table '{TableName}' created or already exists.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Error ensuring table '{TableName}' exists.");
                throw; // Rethrow to indicate failure
            }
        }

        /// <summary>
        /// Retrieves a specific feature setting entity based on PartitionKey (FeatureName) and RowKey (Scope).
        /// </summary>
        /// <param name="featureName">The PartitionKey (e.g., "UserTimeTracking-Feature").</param>
        /// <param name="scopeIdentifier">The RowKey (e.g., "GLOBAL:", "ODS_K81036", "USER_JSMITH").</param>
        /// <returns>The FeatureSettingsEntity if found, otherwise null.</returns>
        public async Task<FeatureSettingsEntity> GetSettingAsync(string featureName, string scopeIdentifier)
        {
            if (string.IsNullOrWhiteSpace(featureName) || string.IsNullOrWhiteSpace(scopeIdentifier))
            {
                _log.LogWarning("GetSettingAsync called with null or empty featureName or scopeIdentifier.");
                return null;
            }

            try
            {
                var response = await _tableClient.GetEntityIfExistsAsync<FeatureSettingsEntity>(featureName, scopeIdentifier);
                return response.HasValue ? response.Value : null;
            }
            catch (RequestFailedException ex)
            {
                _log.LogError(ex, $"Error retrieving setting Feature='{featureName}', Scope='{scopeIdentifier}'. Status: {ex.Status}");
                return null; // Return null on error
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Unexpected error retrieving setting Feature='{featureName}', Scope='{scopeIdentifier}'.");
                return null;
            }
        }

        /// <summary>
        /// Retrieves all setting entities for a specific feature (PartitionKey).
        /// </summary>
        /// <param name="featureName">The PartitionKey (e.g., "UserTimeTracking-Feature").</param>
        /// <returns>A list of FeatureSettingsEntity for the feature, or an empty list if none found or on error.</returns>
        public async Task<List<FeatureSettingsEntity>> GetAllSettingsForFeatureAsync(string featureName)
        {
            var settings = new List<FeatureSettingsEntity>();
            if (string.IsNullOrWhiteSpace(featureName))
            {
                _log.LogWarning("GetAllSettingsForFeatureAsync called with null or empty featureName.");
                return settings;
            }

            try
            {
                string filter = $"PartitionKey eq '{featureName.Replace("'", "''")}'";
                // Escape potential single quotes
                _log.LogInformation($"Querying {TableName} with filter: {filter}");
                await foreach (var entity in _tableClient.QueryAsync<FeatureSettingsEntity>(filter: filter))
                {
                    if (entity != null)
                    {
                        settings.Add(entity);
                    }
                }
                _log.LogInformation($"Found {settings.Count} settings for Feature='{featureName}'.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Error querying settings for Feature='{featureName}'.");
                // Return empty list on error, or rethrow depending on desired behavior
            }
            return settings;
        }

        /// <summary>
        /// Retrieves ALL setting entities from the table.
        /// Use with caution on large tables.
        /// </summary>
        /// <returns>A list of all FeatureSettingsEntity, or an empty list on error.</returns>
        public async Task<List<FeatureSettingsEntity>> GetAllSettingsAsync()
        {
            var settings = new List<FeatureSettingsEntity>();
            _log.LogInformation($"Querying ALL settings from {TableName}.");
            try
            {
                // Query without a filter to get all entities
                await foreach (var entity in _tableClient.QueryAsync<FeatureSettingsEntity>(filter: (string)null))
                {
                    if (entity != null)
                    {
                        settings.Add(entity);
                    }
                }
                _log.LogInformation($"Found {settings.Count} total settings entries.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Error querying all settings from {TableName}.");
                // Return empty list on error
            }
            return settings;
        }

        /// <summary>
        /// Azure Function to Get a distinct list of available feature names (PartitionKeys)
        /// from the FeatureSettings table.
        /// </summary>
        [FunctionName("ListFeatures")]
        public static async Task<IActionResult> ListFeatures(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "featuresettings/listfeatures")] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function 'ListFeatures' processed a request.");
            // TODO: Add Admin Authorization Check here if needed

            string connectionString = Environment.GetEnvironmentVariable("TableStorageConnectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                log.LogError("ListFeatures: Azure Storage Connection String not configured.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            var settingsTable = new FeatureSettingsTable(connectionString, log);
            List<string> featureNames = null;

            try
            {
                // Fetch all settings - This might be inefficient for VERY large tables.
                // Consider alternative strategies (like maintaining a separate list or using projection)
                // if performance becomes an issue with thousands of features/settings.
                // For a moderate number of features, this is simpler.
                var allSettings = await settingsTable.GetAllSettingsAsync(); // Using existing method

                if (allSettings != null)
                {
                    // Extract distinct PartitionKeys (feature names)
                    featureNames = allSettings
                        .Select(entity => entity.PartitionKey)
                        .Distinct(StringComparer.OrdinalIgnoreCase) // Case-insensitive distinct names
                        .OrderBy(name => name) // Order alphabetically
                        .ToList();

                    log.LogInformation($"Found {featureNames.Count} distinct feature names.");
                }
                else
                {
                    log.LogWarning("GetAllSettingsAsync returned null or empty list. Cannot determine feature names.");
                    featureNames = new List<string>(); // Return empty list
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error retrieving or processing settings to list features.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            // Return the list of feature names
            return new OkObjectResult(featureNames);
        }


        /// <summary>
        /// Creates or updates a specific feature setting.
        /// </summary>
        /// <param name="featureName">The PartitionKey.</param>
        /// <param name="scopeIdentifier">The RowKey.</param>
        /// <param name="isEnabled">The value to set for IsEnabled.</param>
        /// <returns>True if successful, false otherwise.</returns>
        public async Task<bool> SetSettingAsync(string featureName, string scopeIdentifier, bool isEnabled)
        {
            if (string.IsNullOrWhiteSpace(featureName) || string.IsNullOrWhiteSpace(scopeIdentifier))
            {
                _log.LogWarning("SetSettingAsync called with null or empty featureName or scopeIdentifier.");
                return false;
            }

            try
            {
                var entity = new FeatureSettingsEntity
                {
                    PartitionKey = featureName,
                    RowKey = scopeIdentifier,
                    IsEnabled = isEnabled,
                    Timestamp = DateTimeOffset.UtcNow, // Set timestamp on update
                    ETag = ETag.All // Use wildcard ETag for UpsertReplace
                };
                // Use UpsertReplace to either insert or replace the existing entity
                await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
                _log.LogInformation($"Successfully upserted setting Feature='{featureName}', Scope='{scopeIdentifier}', IsEnabled={isEnabled}.");
                return true;
            }
            catch (RequestFailedException ex)
            {
                _log.LogError(ex, $"Error upserting setting Feature='{featureName}', Scope='{scopeIdentifier}'. Status: {ex.Status}");
                return false;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Unexpected error upserting setting Feature='{featureName}', Scope='{scopeIdentifier}'.");
                return false;
            }
        }

        /// <summary>
        /// Deletes a specific feature setting.
        /// </summary>
        /// <param name="featureName">The PartitionKey.</param>
        /// <param name="scopeIdentifier">The RowKey.</param>
        /// <returns>True if successful or entity not found, false on error.</returns>
        public async Task<bool> DeleteSettingAsync(string featureName, string scopeIdentifier)
        {
            if (string.IsNullOrWhiteSpace(featureName) || string.IsNullOrWhiteSpace(scopeIdentifier))
            {
                _log.LogWarning("DeleteSettingAsync called with null or empty featureName or scopeIdentifier.");
                return false;
            }

            try
            {
                await _tableClient.DeleteEntityAsync(featureName, scopeIdentifier);
                // ETag defaults to IfMatch any (*)
                _log.LogInformation($"Successfully deleted setting Feature='{featureName}', Scope='{scopeIdentifier}'.");
                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _log.LogWarning($"Setting Feature='{featureName}', Scope='{scopeIdentifier}' not found for deletion.");
                return true; // Consider deletion of non-existent entity a success in this context
            }
            catch (RequestFailedException ex)
            {
                _log.LogError(ex, $"Error deleting setting Feature='{featureName}', Scope='{scopeIdentifier}'. Status: {ex.Status}");
                return false;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Unexpected error deleting setting Feature='{featureName}', Scope='{scopeIdentifier}'.");
                return false;
            }
        }

        /// <summary>
        /// Queries entities from the FeatureSettings table based on an OData filter string.
        /// </summary>
        /// <param name="filter">The OData filter string.</param>
        /// <param name="select">Optional list of properties to select.</param>
        /// <returns>An async stream of FeatureSettingsEntity objects.</returns>
        public async IAsyncEnumerable<FeatureSettingsEntity> QuerySettingsAsync(string filter, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
        {
            _log.LogInformation($"Querying {TableName} with filter: {filter}");
            // Use the internal _tableClient here
            await foreach (var entity in _tableClient.QueryAsync<FeatureSettingsEntity>(filter: filter, select: new List<string> { "PartitionKey", "RowKey" }, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                yield return entity;
            }
        }

        /// <summary>
        /// Submits transaction actions in chunks, handling specific exceptions gracefully.
        /// </summary>
        /// <returns>List of Responses. Null entry indicates chunk failure.</returns>
        public async Task<List<Response<IReadOnlyList<Response>>>> SubmitTransactionInChunksAsync(
                                                                    List<TableTransactionAction> actions,
                                                                    ILogger log,
                                                                    int chunkSize = 100)
        {
            var responses = new List<Response<IReadOnlyList<Response>>>();
            if (actions == null || !actions.Any())
            {
                log?.LogInformation($"SubmitTransactionInChunksAsync: No actions provided to submit for {TableName}.");
                return responses;
            }

            for (int i = 0; i < actions.Count; i += chunkSize)
            {
                var chunk = actions.Skip(i).Take(chunkSize).ToList();
                if (chunk.Any())
                {
                    string firstActionType = chunk.First().ActionType.ToString();
                    try
                    {
                        Response<IReadOnlyList<Response>> response = await _tableClient.SubmitTransactionAsync(chunk).ConfigureAwait(false);
                        responses.Add(response);
                        log?.LogInformation($"Submitted a chunk of {chunk.Count} actions ({firstActionType}) to {TableName}.");
                    }
                    catch (RequestFailedException ex)
                    {
                        // --- REVERTED: Log and re-throw any RequestFailedException ---
                        log?.LogError(ex, $"Failed to submit a chunk transaction ({firstActionType}) to {TableName}. Status: {ex.Status}, ErrorCode: {ex.ErrorCode}, Message: {ex.Message}");
                        responses.Add(null); // Indicate failure for this chunk
                        throw; // Rethrow the original exception
                               // --- END REVERT ---
                    }
                    catch (Exception ex)
                    {
                        log?.LogError(ex, $"Unexpected error submitting a chunk transaction ({firstActionType}) to {TableName}.");
                        responses.Add(null); // Indicate failure for this chunk
                        throw; // Rethrow
                    }
                }
            }
            return responses;
        }
    }


    public class EdgeAlertTables
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
            var entity = new TableEntity(user.UserName.ToUpper(), user.HostName.ToUpper() ?? user.UserName.ToUpper())
            {
                ["ODS"] = user.ODS,
                ["Room"] = user.Room,
                ["Alert"] = user.Alert,
                ["LoggedIn"] = user.LoggedIn,
                ["LastOnline"] = user.LastOnline,
                ["LastPing"] = user.LastPing,
                ["Connected"] = user.Connected,
                ["Version"] = user.Version
            };

            await _tableClient.UpsertEntityAsync(entity);
        }

        public async Task<EdgeUser> GetUserAsync(string userName, string hostName)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<TableEntity>(userName.ToUpper(), hostName.ToUpper() ?? userName.ToUpper());

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
                        LastPing = entity.GetString("LastPing"),
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
                    throw;
                }
            }

            return null;
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
                            UserName = entity.PartitionKey,
                            HostName = entity.RowKey,
                            ODS = entity.GetString("ODS"),
                            Room = entity.GetString("Room"),
                            Alert = entity.GetString("Alert"),
                            LoggedIn = entity.GetString("LoggedIn"),
                            LastOnline = entity.GetString("LastOnline"),
                            LastPing = entity.GetString("LastPing"),
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
                            UserName = entity.PartitionKey,
                            HostName = entity.RowKey,
                            ODS = entity.GetString("ODS"),
                            Room = entity.GetString("Room"),
                            Alert = entity.GetString("Alert"),
                            LoggedIn = entity.GetString("LoggedIn"),
                            LastOnline = entity.GetString("LastOnline"),
                            LastPing = entity.GetString("LastPing"),
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
            await _tableClient.DeleteEntityAsync(userName.ToUpper(), hostName.ToUpper());
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

    public class AlertTables
    {
        public class Alert
        {
            public string? UserName { get; set; }
            public string? HostName {  get; set; }
            public string? ODS { get; set; }
            public string? Room { get; set; }
            public string? AlertStart { get; set; }
            public string? AlertStop { get; set; }
        }

        private readonly TableClient _tableClient;

        public AlertTables(string connectionString, string tableName)
        {
            _tableClient = new TableClient(connectionString, tableName);
        }

        public async Task CreateTableAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task AddOrUpdateAlertAsync(Alert alert)
        {
            var entity = new TableEntity(alert.UserName.ToUpper(), Utils.ConvertDateFormat(alert.AlertStart))
            {
                ["HostName"] = alert.HostName.ToUpper(),
                ["ODS"] = alert.ODS,
                ["Room"] = alert.Room,
                ["AlertStart"] = alert.AlertStart,
                ["AlertStop"] = alert.AlertStop
            };

            await _tableClient.UpsertEntityAsync(entity);
        }

        public async Task<Alert> GetLastAlertAsync(string userName, string hostName, string ods, string room)
        {
            string filter = $"PartitionKey eq '{userName.ToUpper()}' and HostName eq '{hostName.ToUpper()}' and ODS eq '{ods}' and Room eq '{room}'";

            try
            {
                List<Alert> alerts = new List<Alert>();

                await foreach (var entity in _tableClient.QueryAsync<TableEntity>(filter: filter))
                {
                    if (entity != null)
                    {
                        var alert = new Alert
                        {
                            UserName = entity.PartitionKey,
                            HostName = entity.GetString("HostName"),
                            ODS = entity.GetString("ODS"),
                            Room = entity.GetString("Room"),
                            AlertStart = entity.GetString("AlertStart"),
                            AlertStop = entity.GetString("AlertStop")
                        };

                        alerts.Add(alert);
                    }
                }

                var lastAlert = alerts.OrderByDescending(a => DateTime.ParseExact(a.AlertStart, "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)).FirstOrDefault();
                return lastAlert;
            }
            catch (Azure.RequestFailedException ex)
            {
                // Handle exceptions as needed
                throw;
            }
        }

        public async Task UpdateAlertStopAsync(Alert alert)
        {
            try
            {
                var entity = new TableEntity(alert.UserName, Utils.ConvertDateFormat(alert.AlertStart))
                {
                    ["HostName"] = alert.HostName.ToUpper(),
                    ["ODS"] = alert.ODS,
                    ["Room"] = alert.Room,
                    ["AlertStart"] = alert.AlertStart,
                    ["AlertStop"] = alert.AlertStop
                };

                await _tableClient.UpsertEntityAsync(entity);
            }
            catch (Azure.RequestFailedException ex)
            {
                // Handle exceptions as needed
                throw;
            }
        }
    }

    public class ResponderTables
    {
        public class Responder
        {
            public string AlertTime { get; set; }
            public string ResponderName { get; set; }
            public string ResponderHostName { get; set; } // New field
            public string AlertName { get; set; }
            public string ResponderCancelled { get; set; }
        }

        private readonly TableClient _tableClient;

        public ResponderTables(string connectionString, string tableName)
        {
            _tableClient = new TableClient(connectionString, tableName);
        }

        public async Task CreateTableAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task AddOrUpdateResponderAsync(Responder responder)
        {
            var entity = new TableEntity(responder.ResponderName, responder.AlertTime)
            {
                ["ResponderHostName"] = responder.ResponderHostName, // New field is added to entity
                ["AlertName"] = responder.AlertName
            };

            await _tableClient.UpsertEntityAsync(entity);
        }

        // Include additional methods as needed
    }

    public class SystemInfoTables
    {
        public class SystemInfoService
        {
            public string? HostName { get; set; }
            public string? UserName { get; set; }
            public string? LastOnline { get; set; }
            public string? SerialNumber { get; set; }
        }

        private readonly TableClient _tableClient;

        public SystemInfoTables(string connectionString, string tableName)
        {
            _tableClient = new TableClient(connectionString, tableName);
        }

        /// <summary>
        /// Adds or updates System Info in the target table. Uses SerialNumber as the key
        /// if provided, otherwise falls back to using HostName as the key for backward compatibility.
        /// Only updates the LastOnline field, merging with existing data.
        /// </summary>
        public async Task AddOrUpdateSystemInfoAsync(SystemInfoService systemInfo)
        {
            string partitionKey;
            string rowKey;
            string keyIdentifier; // For logging

            // Determine the key based on whether SerialNumber is provided
            if (!string.IsNullOrWhiteSpace(systemInfo.SerialNumber))
            {
                // Use SerialNumber (preferred for new clients/SystemInfo2)
                partitionKey = systemInfo.SerialNumber.ToUpperInvariant();
                rowKey = systemInfo.SerialNumber.ToUpperInvariant();
                keyIdentifier = $"SerialNumber '{partitionKey}'";
            }
            else if (!string.IsNullOrWhiteSpace(systemInfo.HostName))
            {
                // Fallback to HostName (for old clients / SystemInfo table)
                partitionKey = systemInfo.HostName.ToUpperInvariant();
                rowKey = systemInfo.HostName.ToUpperInvariant();
                keyIdentifier = $"HostName '{partitionKey}'";
                Console.WriteLine($"[Warning] AddOrUpdateSystemInfoAsync: SerialNumber was missing for Host '{systemInfo.HostName}'. Using HostName as key (likely targetting old 'SystemInfo' table).");
            }
            else
            {
                // Cannot proceed without a valid key
                Console.WriteLine("[Error] AddOrUpdateSystemInfoAsync: Both SerialNumber and HostName are missing. Cannot update table.");
                return; // Cannot update without a key
            }

            // Create the entity with only the fields to be updated/merged
            var entity = new TableEntity(partitionKey, rowKey)
            {
                ["LastOnline"] = systemInfo.LastOnline
                // Add HostName/UserName ONLY IF you intend the disconnect function
                // to potentially update these fields even when using SerialNumber key.
                // For safety (avoiding overwrites) it's better to only update LastOnline here.
                // If needed:
                // ["HostName"] = systemInfo.HostName,
                // ["UserName"] = systemInfo.UserName?.ToUpperInvariant()
            };

            try
            {
                // Use UpsertEntityAsync with Merge mode to only update specified fields
                await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Merge);
                Console.WriteLine($"[Info] AddOrUpdateSystemInfoAsync: Updated table '{_tableClient.Name}' for {keyIdentifier}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] AddOrUpdateSystemInfoAsync: Failed to upsert entity for {keyIdentifier}. Ex: {ex.Message}");
                // Re-throw or handle as appropriate
                throw;
            }
        }
    }


    public class ServerSideUserStateLogDto
    {
        // Include properties expected from the client JSON payload
        public string Id { get; set; }
        public string UserName { get; set; }
        public string MachineID { get; set; }
        public string ODS { get; set; }
        public string State { get; set; } // Receive State as string
        public DateTime StateStartTime { get; set; }
        public double? DurationSeconds { get; set; }
        public string Context { get; set; }
    }

    // --- UserTimeLog Table Handling ---

    public class UserTimeLogEntity : ITableEntity
    {
        // PartitionKey: Will be set to UserName
        public string PartitionKey { get; set; }

        // RowKey: Will be set to Reverse Ticks + MachineID
        public string RowKey { get; set; }

        // Required by ITableEntity
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        // Custom Properties
        // REMOVED: public string UserID { get; set; }

        public string UserName { get; set; } // Keep
        public string MachineID { get; set; } // Keep

        public string ODS { get; set; }
        public string State { get; set; }
        public double? DurationSeconds { get; set; }
        public string Context { get; set; }
        public DateTime StateStartTime { get; set; }
    }

    public class UserTimeLogTables
    {
        private readonly TableClient _tableClient;
        private const string TableName = "UserTimeLog"; // Define the table name

        public UserTimeLogTables(string connectionString)
        {
            _tableClient = new TableClient(connectionString, TableName);
        }

        // Ensures the table exists in Azure Storage
        public async Task CreateTableAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task<List<UserTimeLogEntity>> GetLogsOlderThanAsync(DateTimeOffset cutoffDate, ILogger log)
        {
            var oldLogs = new List<UserTimeLogEntity>();
            // Construct the filter string. RowKey starts with reverse ticks, so older entries have HIGHER tick values.
            // Max Ticks - Cutoff Ticks gives the threshold. RowKeys greater than this are older.
            string cutoffTicksString = $"{DateTimeOffset.MaxValue.Ticks - cutoffDate.Ticks:D19}";
            // We need RowKeys GREATER than this value because of reverse ticks.
            string filter = $"RowKey gt '{cutoffTicksString}'";
            log.LogInformation($"Querying {TableName} with filter: {filter}");

            try
            {
                await foreach (var entity in _tableClient.QueryAsync<UserTimeLogEntity>(filter: filter))
                {
                    if (entity != null)
                    {
                        oldLogs.Add(entity);
                    }
                }
                log.LogInformation($"Found {oldLogs.Count} log entries older than {cutoffDate}.");
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Error querying logs older than {cutoffDate} from {TableName}.");
                // Handle or rethrow as appropriate
            }
            return oldLogs;
        }

        // --- Helper Method for Submitting Transactions in Chunks ---
        // Add this helper method inside the UserTimeLogTables class
        public async Task<List<Response<IReadOnlyList<Response>>>> SubmitTransactionInChunksAsync(
                                                                                                   List<TableTransactionAction> actions,
                                                                                                   ILogger log,
                                                                                                   int chunkSize = 100)
        {
            var responses = new List<Response<IReadOnlyList<Response>>>();
            for (int i = 0; i < actions.Count; i += chunkSize)
            {
                var chunk = actions.Skip(i).Take(chunkSize).ToList();
                if (chunk.Any())
                {
                    // Determine action type for logging
                    string actionType = chunk.First().ActionType.ToString(); // Get type from first action (usually all same in a batch)

                    try
                    {
                        var response = await _tableClient.SubmitTransactionAsync(chunk);
                        responses.Add(response);
                        // Updated log message
                        log.LogInformation($"Submitted a chunk of {chunk.Count} actions ({actionType}) to {TableName}.");
                    }
                    catch (RequestFailedException ex)
                    {
                        log.LogError($"Failed to submit a chunk transaction ({actionType}) to {TableName}: {ex.Message}. Status: {ex.Status}");
                        responses.Add(null);
                        throw;
                    }
                }
            }
            return responses;
        }

        // NEW METHOD for Phase 2: Get detailed logs within a date range and optionally filtered by user/machine
        public async Task<List<UserTimeLogEntity>> GetDetailedLogsAsync(DateTimeOffset startDate, DateTimeOffset endDate, string userName = null, string machineId = null, ILogger log = null)
        {
            var detailedLogs = new List<UserTimeLogEntity>();

            // With reverse ticks, LARGER date = SMALLER ticks value
            string endTicksValue = $"{DateTimeOffset.MaxValue.Ticks - startDate.Ticks:D19}"; // Upper boundary (older logs)
            string startTicksValue = $"{DateTimeOffset.MaxValue.Ticks - endDate.Ticks:D19}"; // Lower boundary (newer logs)

            // Base filter for date range (using RowKey prefix matching)
            string filter = $"RowKey gt '{startTicksValue}' and RowKey lt '{endTicksValue}_�'";

            // Add PartitionKey filter if userName is provided
            if (!string.IsNullOrWhiteSpace(userName))
            {
                // Ensure userName is properly escaped for the query
                string escapedUserName = userName.Replace("'", "''");
                filter += $" and PartitionKey eq '{escapedUserName}'";
            }

            log?.LogInformation($"Querying {TableName} with filter: {filter}");

            try
            {
                await foreach (var entity in _tableClient.QueryAsync<UserTimeLogEntity>(filter: filter))
                {
                    if (entity != null)
                    {
                        // Additional filtering for MachineID if provided (since it's part of RowKey)
                        if (!string.IsNullOrWhiteSpace(machineId))
                        {
                            // Extract MachineID from RowKey (assuming format ReverseTicks_MachineID)
                            int underscoreIndex = entity.RowKey.IndexOf('_');
                            if (underscoreIndex > 0 && entity.RowKey.Substring(underscoreIndex + 1).Equals(machineId, StringComparison.OrdinalIgnoreCase))
                            {
                                detailedLogs.Add(entity);
                            }
                        }
                        else // No machine ID filter, add all matching the date/user
                        {
                            detailedLogs.Add(entity);
                        }
                    }
                }
                log?.LogInformation($"Found {detailedLogs.Count} detailed log entries for the specified criteria.");
            }
            catch (Exception ex)
            {
                log?.LogError(ex, $"Error querying detailed logs from {TableName} with filter: {filter}.");
                // Handle or rethrow as appropriate
                throw;
            }
            return detailedLogs;
        }
    }

    // --- UserReportAccess Table Handling ---

    public class UserReportAccessEntity : ITableEntity
    {
        // PartitionKey: UserID (e.g., Username_MachineID) who has access
        public string PartitionKey { get; set; }

        // RowKey: Target ODS code the user has access to (or a special value like "*" for all)
        public string RowKey { get; set; }

        // Required by ITableEntity
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        // Custom Properties
        public string AccessorUserID { get; set; } // Repeating for easier querying if needed (Matches PartitionKey)
        public string TargetODS { get; set; } // Repeating for easier querying (Matches RowKey)
        public string PermissionLevel { get; set; } // e.g., "Viewer", "Admin"
    }

    public class UserReportAccessTables
    {
        private readonly TableClient _tableClient;
        private const string TableName = "UserReportAccess"; // Define the table name
        private readonly ILogger _log; // Assuming you might want logging here too

        public UserReportAccessTables(string connectionString, ILogger log)
        {
            _tableClient = new TableClient(connectionString, TableName);
            _log = log;
        }

        // Ensures the table exists in Azure Storage
        public async Task CreateTableAsync()
        {
            try
            {
                await _tableClient.CreateIfNotExistsAsync();
                _log.LogInformation($"Table '{TableName}' created or already exists.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Error ensuring table '{TableName}' exists.");
                throw;
            }
        }

        // Method stubs for Azure Function logic (implementation needed in Functions)
        public async Task AddOrUpdatePermissionAsync(UserReportAccessEntity permission)
        {
            try
            {
                await _tableClient.UpsertEntityAsync(permission, TableUpdateMode.Replace);
                _log.LogInformation($"Upserted permission for User '{permission.PartitionKey}' on ODS '{permission.RowKey}'.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to upsert permission for User '{permission.PartitionKey}' on ODS '{permission.RowKey}'.");
                // Handle exception appropriately
            }
        }

        public async Task RemovePermissionAsync(string accessorUserId, string targetOds)
        {
            try
            {
                await _tableClient.DeleteEntityAsync(accessorUserId, targetOds);
                _log.LogInformation($"Removed permission for User '{accessorUserId}' on ODS '{targetOds}'.");
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _log.LogWarning($"Permission for User '{accessorUserId}' on ODS '{targetOds}' not found for deletion.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to remove permission for User '{accessorUserId}' on ODS '{targetOds}'.");
                // Handle exception appropriately
            }
        }

        public async Task<UserReportAccessEntity> GetPermissionAsync(string accessorUserId, string targetOds)
        {
            try
            {
                var response = await _tableClient.GetEntityIfExistsAsync<UserReportAccessEntity>(accessorUserId, targetOds);
                return response.HasValue ? response.Value : null;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to get permission for User '{accessorUserId}' on ODS '{targetOds}'.");
                return null;
            }
        }

        public async Task<List<UserReportAccessEntity>> GetPermissionsForUserAsync(string accessorUserId)
        {
            var permissions = new List<UserReportAccessEntity>();
            // Ensure accessorUserId is not null or empty before querying
            if (string.IsNullOrWhiteSpace(accessorUserId))
            {
                _log.LogWarning("GetPermissionsForUserAsync: accessorUserId is null or empty.");
                return permissions; // Return empty list
            }

            // Query by PartitionKey (which is the UserID)
            // Escape single quotes in the UserID just in case
            string filter = $"PartitionKey eq '{accessorUserId.Replace("'", "''")}'";
            _log.LogInformation($"Querying {TableName} with filter: {filter}");

            try
            {
                await foreach (var entity in _tableClient.QueryAsync<UserReportAccessEntity>(filter: filter))
                {
                    if (entity != null)
                    {
                        permissions.Add(entity);
                    }
                }
                _log.LogInformation($"Found {permissions.Count} permission entities for User '{accessorUserId}'.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Error querying permissions for User '{accessorUserId}' from {TableName}.");
                // Depending on requirements, you might want to rethrow or return the partial list
                throw; // Rethrow for now to indicate failure
            }
            return permissions;
        }

        // Add helper method for submitting transactions if needed for batch operations later
        public async Task<List<Response<IReadOnlyList<Response>>>> SubmitTransactionInChunksAsync(
          List<TableTransactionAction> actions, ILogger log, int chunkSize = 100)
        {
            // (Implementation similar to other Table classes)
            var responses = new List<Response<IReadOnlyList<Response>>>();
            for (int i = 0; i < actions.Count; i += chunkSize)
            {
                var chunk = actions.Skip(i).Take(chunkSize).ToList();
                if (chunk.Any())
                {
                    try
                    {
                        var response = await _tableClient.SubmitTransactionAsync(chunk);
                        responses.Add(response);
                        log?.LogInformation($"Submitted a chunk of {chunk.Count} actions to {TableName}.");
                    }
                    catch (RequestFailedException ex)
                    {
                        log?.LogError($"Failed to submit a chunk transaction to {TableName}: {ex.Message}. Status: {ex.Status}");
                        responses.Add(null); // Indicate failure
                        throw; // Rethrow to indicate failure at a higher level
                    }
                }
            }
            return responses;
        }
    }

    // --- UserTimeSummary Table Handling ---

    public class UserTimeSummaryEntity : ITableEntity
    {
        // PartitionKey: UserID (e.g., UserName_HostName)
        public string PartitionKey { get; set; }

        // RowKey: Date_ODS (e.g., "YYYYMMDD_A12345")
        public string RowKey { get; set; }

        // Required by ITableEntity
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        // Custom Properties for aggregated data
        public string UserID { get; set; }
        public string ODS { get; set; }
        public DateTime Date { get; set; }
        public double TotalActiveSeconds { get; set; }
        public double TotalAwaySeconds { get; set; }
        public double TotalOfflineSeconds { get; set; }
    }

    public class UserTimeSummaryTables
    {
        private readonly TableClient _tableClient;
        private const string TableName = "UserTimeSummary"; // Define the table name

        public UserTimeSummaryTables(string connectionString)
        {
            _tableClient = new TableClient(connectionString, TableName);
        }

        // Ensures the table exists in Azure Storage
        public async Task CreateTableAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        // Add the same helper method inside the UserTimeSummaryTables class
        public async Task<List<Response<IReadOnlyList<Response>>>> SubmitTransactionInChunksAsync(
           List<TableTransactionAction> actions,
           ILogger log,
           int chunkSize = 100)
        {
            var responses = new List<Response<IReadOnlyList<Response>>>();
            for (int i = 0; i < actions.Count; i += chunkSize)
            {
                var chunk = actions.Skip(i).Take(chunkSize).ToList();
                if (chunk.Any())
                {
                    try
                    {
                        var response = await _tableClient.SubmitTransactionAsync(chunk);
                        responses.Add(response);
                        log.LogInformation($"Submitted a chunk of {chunk.Count} actions to {TableName}.");
                    }
                    catch (RequestFailedException ex)
                    {
                        log.LogError($"Failed to submit a chunk transaction to {TableName}: {ex.Message}. Status: {ex.Status}");
                        responses.Add(null);
                        throw;
                    }
                }
            }
            return responses;
        }


        // NEW METHOD for Phase 2: Get summary logs within a date range and optionally filtered by user
        public async Task<List<UserTimeSummaryEntity>> GetSummaryLogsAsync(DateTime startDate, DateTime endDate, string userName = null, string ods = null, ILogger log = null)
        {
            var summaryLogs = new List<UserTimeSummaryEntity>();

            // Format dates for RowKey filtering - assuming format YYYYMMDD_ODS
            string startDateStr = $"{startDate:yyyyMMdd}";
            string endDateStr = $"{endDate.AddDays(1):yyyyMMdd}"; // Next day for exclusive end

            // Build filter conditions
            List<string> filters = new List<string>();

            // Date filtering in RowKey
            filters.Add($"RowKey ge '{startDateStr}'");
            filters.Add($"RowKey lt '{endDateStr}'"); // Use 'lt' for exclusive upper bound

            // Add PartitionKey filter if userName is provided
            if (!string.IsNullOrWhiteSpace(userName))
            {
                // Ensure userName is properly escaped
                string escapedUserName = userName.Replace("'", "''");
                filters.Add($"PartitionKey eq '{escapedUserName}'");
            }

            // Add ODS filter if provided - CORRECTED to add to filter string
            if (!string.IsNullOrWhiteSpace(ods))
            {
                // Ensure ods is properly escaped
                string escapedOds = ods.Replace("'", "''");
                filters.Add($"ODS eq '{escapedOds}'");
                // Note: Querying on non-key columns like ODS can be less performant on large tables
                // than querying primarily on PartitionKey and RowKey.
            }

            string filter = string.Join(" and ", filters);

            log?.LogInformation($"Querying {TableName} with filter: {filter}");

            try
            {
                await foreach (var entity in _tableClient.QueryAsync<UserTimeSummaryEntity>(filter: filter))
                {
                    if (entity != null)
                    {
                        // No need for post-query ODS filtering anymore
                        summaryLogs.Add(entity);
                    }
                }
                log?.LogInformation($"Found {summaryLogs.Count} summary log entries matching the criteria.");
            }
            catch (Exception ex)
            {
                log?.LogError(ex, $"Error querying summary logs from {TableName} with filter: {filter}.");
                // Handle or rethrow as appropriate
                throw;
            }
            return summaryLogs;
        }
    }
}
