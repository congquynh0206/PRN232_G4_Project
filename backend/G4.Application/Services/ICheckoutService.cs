using G4.Domain.Entities;
using G4.Contracts.Checkout;

namespace G4.Application.Services;

public interface ICheckoutService
{
    Task<IReadOnlyList<Product>> RandomProductsAsync(int count, CancellationToken ct = default);
    Task<PriceQuote> QuoteAsync(int buyerId, CheckoutRequest request, CancellationToken ct = default);
    Task<OrderTable> CreateOrderAsync(int buyerId, CheckoutRequest request, CancellationToken ct = default);
    Task<Payment> PayCardAsync(int orderId, string number, string expiry, string idempotencyKey, CancellationToken ct = default);
    Task<int> ExpirePendingAsync(CancellationToken ct = default);
    Task<int> CloseDeliveredOutsideReturnWindowAsync(CancellationToken ct = default);
    Task<OrderTable> CancelUnpaidAsync(int orderId, CancellationToken ct = default);
    Task QueueEmailAsync(OrderTable order, string eventType, string subject, string body, CancellationToken ct = default);
}
