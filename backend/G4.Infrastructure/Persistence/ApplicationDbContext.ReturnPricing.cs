using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Persistence;

public partial class ApplicationDbContext
{
    private static readonly string[] ReturnPriceProperties =
    [
        nameof(ReturnRequest.PricingPolicyVersion),
        nameof(ReturnRequest.ReasonCode),
        nameof(ReturnRequest.Description),
        nameof(ReturnRequest.RequestKey),
        nameof(ReturnRequest.PricingFingerprint),
        nameof(ReturnRequest.ShippingPayer),
        nameof(ReturnRequest.GoodsPaidSnapshot),
        nameof(ReturnRequest.OutboundShippingRetained),
        nameof(ReturnRequest.ReturnShippingFee),
        nameof(ReturnRequest.BuyerReturnShippingFee),
        nameof(ReturnRequest.BuyerRefundAmount),
        nameof(ReturnRequest.PlatformSubsidyReversal),
        nameof(ReturnRequest.SellerGrossRefund),
        nameof(ReturnRequest.WeightKgSnapshot),
        nameof(ReturnRequest.PickupAddressSnapshot),
        nameof(ReturnRequest.DeliveryAddressSnapshot),
        nameof(ReturnRequest.Reason),
        nameof(ReturnRequest.OrderId),
        nameof(ReturnRequest.UserId),
    ];

    private static void ConfigureReturnPricing(ModelBuilder model)
    {
        model.Entity<ReturnRequest>(e =>
        {
            e.Property(x => x.ReasonCode).HasMaxLength(30);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.RequestKey).HasMaxLength(100);
            e.Property(x => x.PricingFingerprint).HasMaxLength(64);
            e.Property(x => x.ShippingPayer).HasMaxLength(20);
            e.Property(x => x.PickupAddressSnapshot).HasMaxLength(500);
            e.Property(x => x.DeliveryAddressSnapshot).HasMaxLength(500);
            e.Property(x => x.WeightKgSnapshot).HasPrecision(18, 3);
            e.HasIndex(x => x.RequestKey).IsUnique().HasFilter("[RequestKey] IS NOT NULL");
            foreach (
                var name in new[]
                {
                    "GoodsPaidSnapshot",
                    "OutboundShippingRetained",
                    "ReturnShippingFee",
                    "BuyerReturnShippingFee",
                    "BuyerRefundAmount",
                    "PlatformSubsidyReversal",
                    "SellerGrossRefund",
                }
            )
                e.Property<decimal>(name).HasPrecision(18, 2);
        });
        model.Entity<Refund>(e =>
        {
            e.Property(x => x.SellerGrossRefundAmount).HasPrecision(18, 2);
            e.Property(x => x.PlatformSubsidyReversalAmount).HasPrecision(18, 2);
            e.Property(x => x.BuyerReturnShippingRecovery).HasPrecision(18, 2);
        });
        model.Entity<SellerSettlement>(e =>
        {
            e.Property(x => x.ReturnShippingCost).HasPrecision(18, 2);
            e.Property(x => x.ReturnShippingRecovery).HasPrecision(18, 2);
        });
    }

    private void GuardReturnPriceSnapshots()
    {
        foreach (
            var entry in ChangeTracker
                .Entries<ReturnRequest>()
                .Where(e => e.State == EntityState.Modified)
        )
            if (
                entry.OriginalValues.GetValue<int>(nameof(ReturnRequest.PricingPolicyVersion)) == 1
                && ReturnPriceProperties.Any(name => entry.Property(name).IsModified)
            )
                throw new InvalidOperationException(
                    "Báo giá trả hàng đã xác nhận không thể thay đổi."
                );
    }
}
