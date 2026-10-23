using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<AddressChangeRequest> AddressChangeRequests => Set<AddressChangeRequest>();
    public DbSet<SellerShippingPreference> SellerShippingPreferences =>
        Set<SellerShippingPreference>();

    private static void ConfigureAddressChanges(ModelBuilder model)
    {
        model.Entity<AddressChangeRequest>(e =>
        {
            e.ToTable("AddressChangeRequest");
            e.Property(x => x.RequestKey).HasMaxLength(100);
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.DecisionReason).HasMaxLength(1000);
            e.Property(x => x.OldAddress).HasMaxLength(500);
            e.Property(x => x.NewAddress).HasMaxLength(500);
            e.Property(x => x.NewState).HasMaxLength(50);
            e.Property(x => x.NewCountry).HasMaxLength(50);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.QuoteToken).HasMaxLength(64);
            e.HasIndex(x => x.RequestKey).IsUnique();
            e.HasIndex(x => x.OrderId).IsUnique().HasFilter("[IsPending] = 1");
            e.HasIndex(x => new { x.IsPending, x.ExpiresAt });
            e.HasOne<OrderTable>()
                .WithMany()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Refund>()
                .WithMany()
                .HasForeignKey(x => x.RefundId)
                .OnDelete(DeleteBehavior.NoAction);
            foreach (
                var name in new[]
                {
                    nameof(AddressChangeRequest.OldShippingCost),
                    nameof(AddressChangeRequest.NewShippingCost),
                    nameof(AddressChangeRequest.SellerCap),
                    nameof(AddressChangeRequest.BuyerRefund),
                }
            )
                e.Property<decimal>(name).HasColumnType("decimal(18,2)");
        });
        model.Entity<SellerShippingPreference>(e =>
        {
            e.ToTable("SellerShippingPreference");
            e.HasKey(x => x.SellerId);
            e.Property(x => x.AddressChangeCap).HasColumnType("decimal(18,2)");
            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.SellerId)
                .OnDelete(DeleteBehavior.NoAction);
        });
        foreach (
            var name in new[]
            {
                nameof(OrderTable.DeliveryStateSnapshot),
                nameof(OrderTable.DeliveryCountrySnapshot),
                nameof(OrderTable.PickupStateSnapshot),
                nameof(OrderTable.PickupCountrySnapshot),
            }
        )
            model.Entity<OrderTable>().Property<string>(name).HasMaxLength(50);
        model
            .Entity<ShippingInfo>()
            .HasOne<AddressChangeRequest>()
            .WithMany()
            .HasForeignKey(x => x.AddressChangeRequestId)
            .OnDelete(DeleteBehavior.NoAction);
        model
            .Entity<SellerSettlement>()
            .Property(x => x.ShippingAdjustmentCost)
            .HasColumnType("decimal(18,2)");
    }
}
