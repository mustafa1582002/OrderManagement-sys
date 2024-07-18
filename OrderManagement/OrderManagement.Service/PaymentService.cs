using Microsoft.Extensions.Configuration;
using OrderManagement.Core;
using OrderManagement.Core.Entities;
using OrderManagement.Core.Interfaces;
using OrderManagement.Core.Services;
using OrderManagement.Core.Specifications.OrderSpecifications;
using Stripe;
using Order = OrderManagement.Core.Entities.Order;
using Product = OrderManagement.Core.Entities.Product;

namespace OrderManagement.Service
{
    public class PaymentService : IPaymentService
    {
        private readonly IConfiguration _configuration;
        private readonly IUnitOfWork _unitOfWork;

        public PaymentService(IConfiguration configuration,IUnitOfWork unitOfWork)
        {
            _configuration = configuration;
            _unitOfWork = unitOfWork;
        }
        public async Task<OrderWithPaymentDto?> CreateOrUpdatePaymentIntent(OrderWithPaymentDto order)
        {
            StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];
            if(order is null)return null;
            if (order.OrderItems.Count > 0)
            {
                foreach(var item in order.OrderItems)
                {
                    var Product = await _unitOfWork.Repository<Product>().GetByIdAsync(item.ProductId);
                    if (item.UnitPrice != Product.Price)
                        item.UnitPrice = Product.Price;

                }
            }
            var SubTotal = order.OrderItems.Sum(O => O.UnitPrice * O.Quantity);

            var Service = new PaymentIntentService();
            PaymentIntent paymentIntent;
            if (string.IsNullOrEmpty(order.PaymentIntentId))
            {
                var Options = new PaymentIntentCreateOptions()
                {
                    Amount = (long)(SubTotal * 100),
                    Currency = "usd",
                    PaymentMethodTypes = new List<string>() { "card","ideal" }
                };
                paymentIntent = await Service.CreateAsync(Options);
                order.PaymentIntentId = paymentIntent.Id;
                order.ClientSecret = paymentIntent.ClientSecret;
            }
            else
            {
                var Options = new PaymentIntentUpdateOptions()
                {
                    Amount = (long)(SubTotal * 100)
                };
                paymentIntent= await Service.UpdateAsync(order.PaymentIntentId,Options);
                order.PaymentIntentId = paymentIntent.Id;
                order.ClientSecret = paymentIntent.ClientSecret;
            }
            ///Update order first
            return order;
        }

        public async Task<Core.Entities.Order> UpdatePaymentIntentToSucceedOrFailed(string PaymentIntentId, bool flag)
        {
            var Spec = new OrderWithPaymentIntentSpecification(PaymentIntentId);
            var Order = await _unitOfWork.Repository<Order>().GetEntityWithSpecificationAsync(Spec);
            if (flag)
            {
                Order.Status = OrderStatus.PaymentReceived;
            }
            else
            {
                Order.Status = OrderStatus.PaymentFailed;
            }
            _unitOfWork.Repository<Order>().Update(Order);
            await _unitOfWork.CompleteAsync();
            return Order;
        }
    }
}
