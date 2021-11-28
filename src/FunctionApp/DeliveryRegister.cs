using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Newtonsoft.Json;

namespace FunctionApp
{
    public static class DeliveryRegister
    {
        private const string DatabaseName = "Deliveries";
        private const string CollectionName = "Orders";

        [FunctionName("DeliveryRegister")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            [CosmosDB(DatabaseName,
                CollectionName,
                ConnectionStringSetting = "ConnectionStrings:DATABASE_CONNECTION_STRING",
                CreateIfNotExists = true,
                PartitionKey = "/OrderId",
                PreferredLocations = "West US")]
                IAsyncCollector<object> documentsOut)
        {
            var orderId = (string)req.Query["orderId"];
            var total = (string)req.Query["total"];

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync().ConfigureAwait(false);
            var data = JsonConvert.DeserializeObject<dynamic>(requestBody);
            data.OrderId = orderId;
            data.FinalPrice = total;

            await documentsOut.AddAsync(data).ConfigureAwait(false);

            return new OkObjectResult("completed");
        }
    }
}
