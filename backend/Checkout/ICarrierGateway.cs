namespace backend.Checkout;

public interface ICarrierGateway
{
    Task<string> CreateLabelAsync(int orderId, string direction, string key, CancellationToken ct);
}
