using G4.Infrastructure.Notifications;
using G4.Domain.Entities;
using G4.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

public static class NotificationTemplateChecks
{
    public static async Task RunAsync()
    {
        var input = new NotificationTemplateInput(17, "PaymentSucceeded", 0m, "USD", "Promotion", null, null, null, null);
        var zero = NotificationTemplates.Build(input);
        if (!zero.TextBody.Contains("0.00 USD") || !zero.TextBody.Contains("khuyến mãi")) throw new Exception("Zero promotion email lost actual amount or method");
        var encoded = NotificationTemplates.Build(input with { EventType = "DeliveryFailed", Reason = "<script>alert('x')</script>" });
        if (encoded.HtmlBody.Contains("<script>") || !encoded.HtmlBody.Contains("&lt;script&gt;")) throw new Exception("Unsafe email HTML");
        var refunded = NotificationTemplates.Build(input with { EventType = "Refunded", Amount = 15.2m, Method = "Card", ProviderReference = "REF-17" });
        if (!refunded.TextBody.Contains("15.20 USD") || !refunded.TextBody.Contains("REF-17")) throw new Exception("Refund email lost amount/reference");
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Users.Add(new User { Id = 1, Role = "buyer", Email = "buyer@example.test" });
        var order = new OrderTable { Id = 17, BuyerId = 1, SellerId = 2, TotalPrice = 20m, Currency = "USD" };
        db.OrderTables.Add(order);
        await db.SaveChangesAsync();
        db.Payments.Add(new Payment { OrderId = 17, Status = "Succeeded", Method = "Card", Amount = 20m });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Frontend:PublicBaseUrl"]="http://localhost:5131" }).Build();
        var service = new NotificationService(db, config);
        await service.EnqueueAsync(order, "PaymentSucceeded", default);
        await service.EnqueueAsync(order, "PaymentSucceeded", default);
        if (db.NotificationOutbox.Local.Count != 1 || !db.NotificationOutbox.Local.Single().Body.Contains("20.00 USD")) throw new Exception("Outbox must see tracked successful payment and dedupe unsaved events");
        await db.SaveChangesAsync();
        await service.EnqueueAsync(order, "PaymentSucceeded", default);
        if (await db.NotificationOutbox.CountAsync() != 1) throw new Exception("Outbox replay duplicated email");
        db.Users.Local.Single().Email = null;
        await db.SaveChangesAsync();
        await service.EnqueueAsync(order, "Delivered", default);
        var invalid = db.NotificationOutbox.Local.Single(x=>x.EventType=="Delivered");
        if (invalid.Status != "Failed" || invalid.Recipient.Length != 0) throw new Exception("Missing recipient must not fallback to another email");
        Console.WriteLine("Notification templates, zero payment, snapshot and recipient checks passed");
    }
}
