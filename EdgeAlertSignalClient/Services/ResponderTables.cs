using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Azure.Data.Tables;
using EdgeAlertSignalClient.Utilities;

namespace EdgeAlertSignalClient.Services
{
    public class ResponderTables
    {
        private readonly TableClient _tableClient;

        public ResponderTables(string connectionString, string tableName)
        {
            _tableClient = new TableClient(connectionString, tableName);
        }

        public async Task CreateTableAsync()
        {
            await _tableClient.CreateIfNotExistsAsync();
        }

        public async Task AddResponderAsync(Responder responder)
        {
            var entity = new TableEntity(responder.ResponderName.ToUpper(), Utils.ConvertDateFormat(responder.AlertTime))
            {
                ["AlertName"] = responder.AlertName,
                ["ResponderHostName"] = responder.ResponderHostName
            };

            await _tableClient.AddEntityAsync(entity);
        }

        public async Task<List<Responder>> GetAllRespondersAsync()
        {
            List<Responder> responders = new List<Responder>();
            await foreach (var entity in _tableClient.QueryAsync<TableEntity>(select: new[] { "PartitionKey", "RowKey", "AlertName", "ResponderCancelled", "ResponderHostName" }))
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
            return responders;
        }

        public async Task<List<Responder>> GetRespondersByODSAsync(string ods)
        {
            List<Responder> responders = new List<Responder>();
            await foreach (var entity in _tableClient.QueryAsync<TableEntity>($"ODS eq '{ods}'", select: new[] { "PartitionKey", "RowKey", "AlertName", "ResponderCancelled", "ResponderHostName" }))
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
            return responders;
        }

        public async Task<ObservableCollection<Responder>> GetRespondersByAlertTimeAsync(string alertTime)
        {
            ObservableCollection<Responder> responders = new ObservableCollection<Responder>();

            try
            {
                await foreach (var entity in _tableClient.QueryAsync<TableEntity>($"RowKey eq '{alertTime}'", select: new[] { "PartitionKey", "RowKey", "AlertName", "ResponderCancelled", "ResponderHostName" }))
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
    }
}
