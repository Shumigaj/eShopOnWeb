using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace FunctionApp
{
    public static class OrderItemsReserver
    {
        private static HttpClient httpClient = new HttpClient();

        [FunctionName("OrderItemsReserver")]
        public static async Task Run([ServiceBusTrigger("orders", Connection = "ORDERS_QUEUE_CONNECTION_STRING")] string myQueueItem,
            ILogger log,
            ExecutionContext context)
        {
            var orderStored = false;
            var attemptNumber = 0;

            do
            {
                try
                {
                    log.LogInformation("Reserve request received.");
                    await StoreReserveRequest(myQueueItem, context).ConfigureAwait(false);
                    orderStored = true;
                }
                catch (Exception e)
                {
                    log.LogError(e, $"Failed to process '{nameof(OrderItemsReserver)}' Azure function");
                    await Task.Delay(5000).ConfigureAwait(false);
                }
                finally
                {
                    attemptNumber++;
                }
            } while (!orderStored && attemptNumber < 3);

            if (!orderStored)
            {
                await BackUpProcessing(myQueueItem, context).ConfigureAwait(false);
            }
        }

        private static async Task BackUpProcessing(string myQueueItem, ExecutionContext context)
        {
            var logicAppUri = GetConfiguration(context)["SEND_EMAIL_ORDER_SERVICE_URL"];
            await httpClient.PostAsync(logicAppUri, new StringContent(myQueueItem, Encoding.UTF8, "application/json"));
        }

        private static async Task StoreReserveRequest(string reserveRequestData, ExecutionContext context)
        {
            var container = await GetOrCreateContainer(context, "reserved-items").ConfigureAwait(false);

            var blob = container.GetBlockBlobReference($"{Guid.NewGuid()}.json");

            blob.Properties.ContentType = "application/json";

            await blob.UploadTextAsync(reserveRequestData).ConfigureAwait(false);

            await blob.SetPropertiesAsync().ConfigureAwait(false);
        }

        private static async Task<CloudBlobContainer> GetOrCreateContainer(ExecutionContext context, string containerName)
        {
            var storageAccount = GetCloudStorageAccount(context);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var blobContainer = blobClient.GetContainerReference(containerName);
            await blobContainer.CreateIfNotExistsAsync();
            return blobContainer;
        }

        private static CloudStorageAccount GetCloudStorageAccount(ExecutionContext context)
            => CloudStorageAccount.Parse(GetConfiguration(context)["STORAGE_ACCOUNT_CONNECTION_STRING"]);

        private static IConfigurationRoot GetConfiguration(ExecutionContext context)
        {
            return new ConfigurationBuilder()
                            .SetBasePath(context.FunctionAppDirectory)
                            .AddJsonFile("local.settings.json", true, true)
                            .AddEnvironmentVariables()
                            .Build();
        }
    }
}
