using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Azure.Data.Tables;
using EdgeAlertSignalClient.Handlers;
using static EdgeAlertSignalClient.Services.AlertTables;

namespace EdgeAlertSignalClient.Services
{
    public class JoinTables
    {
        public class AlertHistory
        {
            public string RaisedWhen { get; set; }
            public string RaisedBy { get; set; }
            public string Device { get; set; }
            public string Location { get; set; }
            public string Responder { get; set; }
            public string TimeToRespond { get; set; }
            public string DateFrom { get; set; }
            public string DateTo { get; set; }
            public string Practice { get; set; }
        }

        private readonly AlertTables _alertsTableClient;
        private readonly ResponderTables _respondersTableClient;
        private readonly EdgeAlertTables _edgeAlertTables;

        private readonly JSONHandler _jsonHandler;

        public JoinTables(string connectionString, 
                          string alertTableName, 
                          string responderTablename,
                          string usersTableName,
                          JSONHandler jsonHandler)
        {
            _alertsTableClient = new AlertTables(connectionString, alertTableName);
            _respondersTableClient = new ResponderTables(connectionString, responderTablename);
            _edgeAlertTables = new EdgeAlertTables(connectionString, usersTableName);
            _jsonHandler = jsonHandler;
        }

        public async Task<ObservableCollection<AlertHistory>> GetCombinedDataAsync(string ods)
        {
            var similarODSList = _jsonHandler.GetSimilarODS(ods);
            var combinedData = new List<AlertHistory>();

            // Get ALL responders once - no ODS filtering
            var allResponders = await _respondersTableClient.GetAllRespondersAsync();

            foreach (var similarODS in similarODSList)
            {
                // Only filter alerts by ODS
                var alerts = await _alertsTableClient.GetAllAlertsByODSAsync(similarODS);

                var partialData = from alert in alerts
                                      // Create a local collection of matching responders for this alert
                                  let matchingResponders = allResponders.Where(r => r.AlertTime == alert.RowKey).ToList()
                                  let practice = _jsonHandler.GetLocationByODS(alert.ODS)?.Practice
                                  select new AlertHistory
                                  {
                                      RaisedWhen = alert.AlertStart,
                                      RaisedBy = alert.UserName,
                                      Device = alert.HostName,
                                      Location = alert.Room,
                                      // Format multiple responders if they exist
                                      Responder = matchingResponders.Any()
                                          ? string.Join(", ", matchingResponders.Select(r => r.ResponderName))
                                          : null,
                                      TimeToRespond = CalculateTimeToRespond(alert.AlertStart, alert.AlertStop),
                                      DateFrom = alert.AlertStart,
                                      DateTo = alert.AlertStop,
                                      Practice = practice
                                  };

                combinedData.AddRange(partialData);
            }

            return new ObservableCollection<AlertHistory>(combinedData);
        }

        public async Task<ObservableCollection<EdgeUserViewModel>> GetAllUsersBySimilarODSAsync(string TrueODS)
        {
            var similarODSList = _jsonHandler.GetSimilarODS(TrueODS);
            var allUsers = new List<EdgeUser>();

            foreach (var similarODS in similarODSList)
            {
                var usersForThisODS = await _edgeAlertTables.GetFilteredUsersAsync(similarODS);
                allUsers.AddRange(usersForThisODS);
            }

            // Convert the EdgeUser objects to EdgeUserViewModel objects
            var userViewModels = allUsers.Select(user =>
            {
                var edgeUserViewModel = new EdgeUserViewModel
                {
                    UserName = user.UserName,
                    HostName = user.HostName,
                    ODS = _jsonHandler.GetLocationByODS(user.ODS)?.Practice,
                    Room = user.Room,
                    Alert = user.Alert,
                    LoggedIn = user.LoggedIn,
                    LastOnline = user.LastOnline,
                    Connected = user.Connected,
                    Version = user.Version
                };

                return edgeUserViewModel;
            }).ToList();

            return new ObservableCollection<EdgeUserViewModel>(userViewModels);
        }


        private string CalculateTimeToRespond(string start, string stop)
        {
            DateTimeFormatInfo dtfi = new DateTimeFormatInfo
            {
                ShortDatePattern = "dd/MM/yyyy",
                LongTimePattern = "HH:mm:ss"
            };

            if (DateTime.TryParseExact(start, "dd/MM/yyyy HH:mm:ss", dtfi, DateTimeStyles.None, out DateTime startDateTime) &&
                DateTime.TryParseExact(stop, "dd/MM/yyyy HH:mm:ss", dtfi, DateTimeStyles.None, out DateTime stopDateTime))
            {
                var timeToRespond = stopDateTime - startDateTime;

                // Convert the TimeSpan to a string format as you wish
                return timeToRespond.ToString(@"hh\:mm\:ss");
            }
            else
            {
                // Handle the case where the date string is not in expected format
                return "Invalid time format";
            }
        }
    }

}
