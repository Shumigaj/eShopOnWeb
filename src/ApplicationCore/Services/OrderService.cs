using Ardalis.GuardClauses;
using Azure.Messaging.ServiceBus;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services
{
    public class OrderService : IOrderService
    {
        private readonly IAsyncRepository<Order> _orderRepository;
        private readonly IUriComposer _uriComposer;
        private readonly IAsyncRepository<Basket> _basketRepository;
        private readonly IAsyncRepository<CatalogItem> _itemRepository;

        public OrderService(IAsyncRepository<Basket> basketRepository,
            IAsyncRepository<CatalogItem> itemRepository,
            IAsyncRepository<Order> orderRepository,
            IUriComposer uriComposer)
        {
            _orderRepository = orderRepository;
            _uriComposer = uriComposer;
            _basketRepository = basketRepository;
            _itemRepository = itemRepository;
        }

        public async Task CreateOrderAsync(int basketId, Address shippingAddress,
            string deliveryRegisterFunctionUrl, string ordersQueueConnetionString)
        {
            var basketSpec = new BasketWithItemsSpecification(basketId);
            var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

            Guard.Against.NullBasket(basketId, basket);
            Guard.Against.EmptyBasketOnCheckout(basket.Items);

            var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
            var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

            var items = basket.Items.Select(basketItem =>
            {
                var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
                var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
                var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
                return orderItem;
            }).ToList();

            var order = new Order(basket.BuyerId, shippingAddress, items);

            var submittedOrder = await _orderRepository.AddAsync(order);
            await SendDeliveryRequest(submittedOrder, deliveryRegisterFunctionUrl);
            await SendOrderToQueue(submittedOrder, ordersQueueConnetionString);
        }

        private static readonly HttpClient Client = new HttpClient();

        private async Task SendDeliveryRequest(Order order, string deliveryRegisterFunctionUrl)
        {
            var requestUrl = $"{deliveryRegisterFunctionUrl}&orderId={order.Id}&total={order.Total()}";

            var orderJson = JsonConvert.SerializeObject(order);
            var requestData = new StringContent(orderJson, Encoding.UTF8, "application/json");
            await Client.PostAsync(requestUrl, requestData).ConfigureAwait(false);
        }

        private async Task SendOrderToQueue(Order order, string ordersQueueConnetionString)
        {
            // name of your Service Bus queue
            var queueName = "orders";

            var orderJson = JsonConvert.SerializeObject(order);

            var client = new ServiceBusClient(ordersQueueConnetionString);
            var sender = client.CreateSender(queueName);

            // create a batch
            using ServiceBusMessageBatch messageBatch = await sender.CreateMessageBatchAsync();

            // try adding a message to the batch
            if (!messageBatch.TryAddMessage(new ServiceBusMessage(orderJson)))
            {
                // if it is too large for the batch
                throw new Exception($"The message is too large to fit in the batch.");
            }

            try
            {
                // Use the producer client to send the batch of messages to the Service Bus queue
                await sender.SendMessagesAsync(messageBatch);
            }
            finally
            {
                // Calling DisposeAsync on client types is required to ensure that network
                // resources and other unmanaged objects are properly cleaned up.
                await sender.DisposeAsync();
                await client.DisposeAsync();
            }
        }
    }
}
