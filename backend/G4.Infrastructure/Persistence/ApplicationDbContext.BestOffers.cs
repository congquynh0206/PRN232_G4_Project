using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Persistence;

public partial class ApplicationDbContext
{
    private static void ConfigureBestOffers(ModelBuilder model)
    {
        model.Entity<Product>(entity =>
        {
            entity.Property(x => x.AutoAcceptOfferPrice).HasColumnType("decimal(10,2)");
            entity.Property(x => x.AutoDeclineOfferBelow).HasColumnType("decimal(10,2)");
        });

        model.Entity<BestOffer>(entity =>
        {
            entity.ToTable("BestOffer");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(24);
            entity.Property(x => x.PaymentMethod).HasMaxLength(20);
            entity.Property(x => x.CardProfile).HasMaxLength(20);
            entity.Property(x => x.CardExpiry).HasMaxLength(5);
            entity.Property(x => x.CreationKey).HasMaxLength(100);
            entity.Property(x => x.CurrentAmount).HasColumnType("decimal(10,2)");
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => new { x.ProductId, x.BuyerId, x.Status });
            entity.HasIndex(x => new { x.BuyerId, x.UpdatedAt });
            entity.HasIndex(x => new { x.SellerId, x.Status, x.UpdatedAt });
            entity.HasIndex(x => x.CreationKey).IsUnique();
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.BuyerId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.SellerId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.CurrentActorId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<Address>().WithMany().HasForeignKey(x => x.AddressId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<OrderTable>().WithMany().HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        model.Entity<BestOfferTurn>(entity =>
        {
            entity.ToTable("BestOfferTurn");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasColumnType("decimal(10,2)");
            entity.Property(x => x.Action).HasMaxLength(20);
            entity.Property(x => x.Result).HasMaxLength(24);
            entity.Property(x => x.RequestKey).HasMaxLength(100);
            entity.HasIndex(x => new { x.BestOfferId, x.Id });
            entity.HasIndex(x => x.RequestKey).IsUnique().HasFilter("[RequestKey] IS NOT NULL");
            entity.HasOne(x => x.BestOffer).WithMany().HasForeignKey(x => x.BestOfferId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ActorId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
