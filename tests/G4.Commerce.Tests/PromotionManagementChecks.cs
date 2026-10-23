using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;

internal static class PromotionManagementChecks
{
    public static async Task RunAsync()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        db.Users.AddRange(
            new User { Id = 1, Role = "seller" },
            new User { Id = 2, Role = "seller" },
            new User { Id = 3, Role = "admin" }
        );
        db.Products.AddRange(
            new Product
            {
                Id = 1,
                SellerId = 1,
                Price = 100,
            },
            new Product
            {
                Id = 2,
                SellerId = 2,
                Price = 100,
            }
        );
        await db.SaveChangesAsync();
        var service = new PromotionManagementService(db);
        var input = new PromotionInput
        {
            Name = "Giảm shop",
            Type = "Coupon",
            Code = " hello10 ",
            Value = 10,
            IsPercent = true,
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(1),
            ProductIds = [1],
        };
        var created = await service.SaveAsync(1, false, input);
        ReturnAutomationChecks.Check(
            created.Code == "HELLO10" && created.SellerId == 1 && created.FundingSource == "Seller",
            "promotion owner and normalized code"
        );
        await Reject<UnauthorizedAccessException>(() =>
            service.SaveAsync(
                2,
                false,
                input with
                {
                    Name = "Ghi đè",
                    ProductIds = [2],
                    Version = created.Version,
                },
                created.Id
            )
        );
        await Reject<UnauthorizedAccessException>(() =>
            service.SaveAsync(1, false, input with { Code = "FOREIGN", ProductIds = [2] })
        );
        await Reject<InvalidOperationException>(() =>
            service.SaveAsync(2, false, input with { ProductIds = [2] })
        );
        await Reject<ArgumentException>(() =>
            service.SaveAsync(1, false, input with { Code = "ZERO", Value = 0 })
        );
        await Reject<ArgumentException>(() =>
            service.SaveAsync(3, true, input with { Type = "Sale", Code = null })
        );
        await Reject<UnauthorizedAccessException>(() =>
            service.SetStateAsync(
                3,
                true,
                created.Id,
                new PromotionStateInput(false, null, created.Version)
            )
        );
        await Reject<UnauthorizedAccessException>(() =>
            service.SetStateAsync(
                3,
                true,
                created.Id,
                new PromotionStateInput(true, "Vi phạm", created.Version)
            )
        );
        var paused = await service.SetSuspensionAsync(
            3,
            created.Id,
            new PromotionSuspensionInput(true, "Vi phạm", created.Version)
        );
        ReturnAutomationChecks.Check(
            paused.AdminPaused && paused.Status == "Paused",
            "admin pause records authority"
        );
        await Reject<UnauthorizedAccessException>(() =>
            service.SetStateAsync(
                1,
                false,
                created.Id,
                new PromotionStateInput(false, null, paused.Version)
            )
        );
        await Reject<InvalidOperationException>(() =>
            service.SaveAsync(1, false, input with { Version = created.Version }, created.Id)
        );
        var foreignList = await service.ListAsync(2, false);
        ReturnAutomationChecks.Check(
            foreignList.TotalCount == 0,
            "seller cannot list another shop promotion"
        );
        ReturnAutomationChecks.Check(
            await db.PromotionAudits.CountAsync() == 2,
            "create and admin pause are audited"
        );
        await Reject<UnauthorizedAccessException>(() =>
            service.SetSuspensionAsync(1, created.Id, new(false, "Tự gỡ", paused.Version))
        );
        await Reject<ArgumentException>(() =>
            service.SetSuspensionAsync(3, created.Id, new(false, " ", paused.Version))
        );
        await Reject<UnauthorizedAccessException>(() =>
            service.SaveAsync(3, true, input with { Version = paused.Version }, created.Id)
        );
        await Reject<UnauthorizedAccessException>(() =>
            service.SaveAsync(
                1,
                false,
                input with
                {
                    Version = paused.Version,
                    IsPaused = false,
                },
                created.Id
            )
        );
        var released = await service.SetSuspensionAsync(
            3,
            created.Id,
            new(false, "Đã khắc phục", paused.Version)
        );
        ReturnAutomationChecks.Check(
            !released.AdminPaused && released.IsPaused && released.Status == "Paused",
            "release never activates seller program"
        );
        await Reject<UnauthorizedAccessException>(() =>
            service.SetStateAsync(3, true, created.Id, new(false, null, released.Version))
        );
        var resumed = await service.SetStateAsync(
            1,
            false,
            created.Id,
            new(false, null, released.Version)
        );
        ReturnAutomationChecks.Check(
            !resumed.IsPaused && resumed.Status == "Active",
            "seller explicitly resumes after release"
        );
        var selfPaused = await service.SetStateAsync(
            1,
            false,
            created.Id,
            new(true, "Shop tạm dừng", resumed.Version)
        );
        await Reject<InvalidOperationException>(() =>
            service.SetSuspensionAsync(
                3,
                created.Id,
                new(false, "Không được bật hộ shop", selfPaused.Version)
            )
        );
        ReturnAutomationChecks.Check(
            (await service.ReadAsync(1, false, created.Id)).IsPaused,
            "admin cannot activate seller's own pause"
        );
        var blockedAgain = await service.SetSuspensionAsync(
            3,
            created.Id,
            new(true, "Cần rà soát", selfPaused.Version)
        );
        var sellerStillPaused = await service.SetStateAsync(
            1,
            false,
            created.Id,
            new(true, null, blockedAgain.Version)
        );
        ReturnAutomationChecks.Check(
            sellerStillPaused.AdminPaused && sellerStillPaused.PauseReason == "Cần rà soát",
            "seller pause preserves admin suspension reason"
        );
        var platform = await service.SaveAsync(
            3,
            true,
            input with
            {
                Code = "PLATFORM",
                ProductIds = [],
                SellerId = null,
            }
        );
        var platformPaused = await service.SetStateAsync(
            3,
            true,
            platform.Id,
            new(true, null, platform.Version)
        );
        var platformResumed = await service.SetStateAsync(
            3,
            true,
            platform.Id,
            new(false, null, platformPaused.Version)
        );
        ReturnAutomationChecks.Check(
            platformResumed.Status == "Active" && !platformResumed.AdminPaused,
            "admin owns platform coupon activation"
        );
        await Reject<UnauthorizedAccessException>(() =>
            service.SetSuspensionAsync(
                3,
                platform.Id,
                new(true, "Wrong endpoint", platformResumed.Version)
            )
        );
        await Reject<UnauthorizedAccessException>(() =>
            service.SetStateAsync(1, false, platform.Id, new(true, null, platformResumed.Version))
        );
        ReturnAutomationChecks.Check(
            await db.PromotionAudits.AnyAsync(x =>
                x.PromotionId == created.Id && x.Action == "Suspend"
            )
                && await db.PromotionAudits.AnyAsync(x =>
                    x.PromotionId == created.Id && x.Action == "ReleaseSuspension"
                ),
            "suspension actions have separate audit records"
        );
        db.Users.Add(new User { Id = 4, Role = "buyer" });
        db.Products.Local.Single(x => x.Id == 1).WeightKg = 1;
        db.Inventories.Add(new Inventory { ProductId = 1, Quantity = 10 });
        db.Addresses.AddRange(
            new Address
            {
                Id = 4,
                UserId = 4,
                State = "Hanoi",
                Country = "Vietnam",
            },
            new Address
            {
                Id = 5,
                UserId = 1,
                State = "Hanoi",
                Country = "Vietnam",
                IsDefault = true,
            }
        );
        await db.SaveChangesAsync();
        var checkout = new CheckoutService(db);
        var cart = new CheckoutRequest(4, [new(1, 1)], created.Code, "moderation-quote");
        await Reject<ArgumentException>(() => checkout.QuoteAsync(4, cart));
        var unblocked = await service.SetSuspensionAsync(
            3,
            created.Id,
            new(false, "Đã khắc phục", sellerStillPaused.Version)
        );
        await Reject<ArgumentException>(() => checkout.QuoteAsync(4, cart));
        await service.SetStateAsync(1, false, created.Id, new(false, null, unblocked.Version));
        ReturnAutomationChecks.Check(
            (await checkout.QuoteAsync(4, cart)).Total == 92m,
            "discount applies only after seller explicitly resumes"
        );
        Console.WriteLine(
            "Promotion management ownership, moderation separation, validation and audit checks passed"
        );
    }

    private static async Task Reject<T>(Func<Task> work)
        where T : Exception
    {
        try
        {
            await work();
        }
        catch (T)
        {
            return;
        }

        throw new Exception($"Expected {typeof(T).Name}");
    }
}
