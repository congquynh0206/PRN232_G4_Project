using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Persistence;
public partial class ApplicationDbContext
{
    public DbSet<BuyerCart> BuyerCarts => Set<BuyerCart>();
    public DbSet<CheckoutBatch> CheckoutBatches => Set<CheckoutBatch>();

    private static void ConfigureCart(ModelBuilder model)
    {
        model.Entity<BuyerCart>(e =>
        {
            e.ToTable("BuyerCart");
            e.HasKey(x => x.BuyerId);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.BuyerId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<CheckoutBatch>(e =>
        {
            e.ToTable("CheckoutBatch");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(80);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.BuyerId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
