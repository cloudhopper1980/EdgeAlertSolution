using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Data.Tables;

namespace EdgeAlertSignalClient.Services
{
    public class AlertTables
    {
        public class Alert
        {
            public string? UserName { get; set; }
            public string? HostName { get; set; }
            public string? RowKey { get; set; }
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

        public async Task<List<Alert>> GetAllAlertsByODSAsync(string ods)
        {
            string filter = $"ODS eq '{ods}'";
            List<Alert> alerts = new List<Alert>();

            await foreach (var entity in _tableClient.QueryAsync<TableEntity>(filter: filter))
            {
                if (entity != null)
                {
                    var alert = new Alert
                    {
                        UserName = entity.PartitionKey,
                        HostName = entity.GetString("HostName"),
                        RowKey = entity.GetString("RowKey"),
                        ODS = entity.GetString("ODS"),
                        Room = entity.GetString("Room"),
                        AlertStart = entity.GetString("AlertStart"),
                        AlertStop = entity.GetString("AlertStop")
                    };

                    alerts.Add(alert);
                }
            }

            return alerts;
        }
    }
}
