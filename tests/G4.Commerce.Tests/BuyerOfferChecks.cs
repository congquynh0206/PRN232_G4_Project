using System.Security.Claims;
using System.Text.Json;
using G4.Api.Controllers;
using G4.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.Internal;

internal static class BuyerOfferChecks
{
    public static async Task RunAsync()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options
        );
        var now = DateTime.UtcNow;
        Promotion Coupon(string code, int? seller, bool published = true) => new()
        {
            Type = "Coupon",
            Code = code,
            SellerId = seller,
            IsPublicCoupon = published,
            StartAt = now.AddDays(-1),
            EndAt = now.AddDays(1),
        };
        var paused = Coupon("PAUSED", 1);
        paused.AdminPaused = true;
        var expired = Coupon("EXPIRED", 1);
        expired.EndAt = now.AddHours(-1);
        var exhausted = Coupon("EXHAUSTED", 1);
        exhausted.MaxUsage = 0;
        db.Promotions.AddRange(
            Coupon("SHOP", 1), Coupon("GLOBAL", null), Coupon("OTHER", 2),
            Coupon("PRIVATE", 1, false), paused, expired, exhausted
        );
        await db.SaveChangesAsync();

        var controller = new PromotionsController(
            db, new HostingEnvironment { EnvironmentName = "Development" }
        );
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = User("buyer") },
        };
        var publicCodes = Codes(await controller.Offers("buyer", type: "Coupon"));
        if (!publicCodes.SetEquals(["SHOP", "GLOBAL", "OTHER"]))
            throw new Exception("Discovery must only expose active, public, available coupons.");
        var sellerCodes = Codes(await controller.Offers("buyer", type: "Coupon", sellerId: 1));
        if (!sellerCodes.SetEquals(["SHOP", "GLOBAL", "EXHAUSTED"]))
            throw new Exception("Checkout must scope codes to the selected seller and platform.");
        controller.HttpContext.User = User("seller");
        if (await controller.Offers("buyer") is not StatusCodeResult { StatusCode: 403 })
            throw new Exception("Only buyers can access the buyer offers endpoint.");
        Console.WriteLine("Buyer offers: role, visibility, active window, limits and shop scope passed");
    }

    private static ClaimsPrincipal User(string role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));

    private static HashSet<string?> Codes(IActionResult result) =>
        JsonSerializer.SerializeToElement(((OkObjectResult)result).Value)
            .GetProperty("Items").EnumerateArray()
            .Select(item => item.GetProperty("Code").GetString()).ToHashSet();
}
