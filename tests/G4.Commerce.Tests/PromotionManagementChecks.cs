using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;

internal static class PromotionManagementChecks
{
    public static async Task RunAsync()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Users.AddRange(new User { Id = 1, Role = "seller" }, new User { Id = 2, Role = "seller" }, new User { Id = 3, Role = "admin" });
        db.Products.AddRange(new Product { Id = 1, SellerId = 1, Price = 100 }, new Product { Id = 2, SellerId = 2, Price = 100 });
        await db.SaveChangesAsync();
        var service = new PromotionManagementService(db);
        var input = new PromotionInput { Name = "Giảm shop", Type = "Coupon", Code = " hello10 ", Value = 10, IsPercent = true, StartAt = DateTime.UtcNow.AddDays(-1), EndAt = DateTime.UtcNow.AddDays(1), ProductIds = [1] };
        var created = await service.SaveAsync(1, false, input);
        ReturnAutomationChecks.Check(created.Code == "HELLO10" && created.SellerId == 1 && created.FundingSource == "Seller", "promotion owner and normalized code");
        await Reject<UnauthorizedAccessException>(() => service.SaveAsync(2, false, input with { Name = "Ghi đè", ProductIds = [2], Version = created.Version }, created.Id));
        await Reject<UnauthorizedAccessException>(() => service.SaveAsync(1, false, input with { Code = "FOREIGN", ProductIds = [2] }));
        await Reject<InvalidOperationException>(() => service.SaveAsync(2, false, input with { ProductIds = [2] }));
        await Reject<ArgumentException>(() => service.SaveAsync(1, false, input with { Code = "ZERO", Value = 0 }));
        await Reject<ArgumentException>(() => service.SaveAsync(3, true, input with { Type = "Sale", Code = null }));
        var paused = await service.SetStateAsync(3, true, created.Id, new PromotionStateInput(true, "Vi phạm", created.Version));
        ReturnAutomationChecks.Check(paused.AdminPaused && paused.Status == "Paused", "admin pause records authority");
        await Reject<UnauthorizedAccessException>(() => service.SetStateAsync(1, false, created.Id, new PromotionStateInput(false, null, paused.Version)));
        await Reject<InvalidOperationException>(() => service.SaveAsync(1, false, input with { Version = created.Version }, created.Id));
        var foreignList = await service.ListAsync(2, false);
        ReturnAutomationChecks.Check(foreignList.TotalCount == 0, "seller cannot list another shop promotion");
        ReturnAutomationChecks.Check(await db.PromotionAudits.CountAsync() == 2, "create and admin pause are audited");
        Console.WriteLine("Promotion management ownership, validation and audit checks passed");
    }
    private static async Task Reject<T>(Func<Task> work) where T : Exception
    { try { await work(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
}
