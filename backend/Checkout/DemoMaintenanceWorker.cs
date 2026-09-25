using System.Net;
using System.Net.Mail;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Checkout;

public sealed class DemoMaintenanceWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<DemoMaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var verifyingOrderIds = await db.Payments.Where(p => p.Method == "PayPal" && p.Status == "Verifying")
                    .Select(p => p.OrderId).Distinct().Take(20).ToListAsync(stoppingToken);
                var paypal = scope.ServiceProvider.GetRequiredService<PayPalPaymentService>();
                foreach (var orderId in verifyingOrderIds)
                {
                    if (orderId is null) continue;
                    try { await paypal.ReconcileAsync(orderId.Value, stoppingToken); }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                    {
                        logger.LogWarning(ex, "PayPal reconciliation pending for order {OrderId}", orderId);
                    }
                }
                await new MvpService(db).ExpirePendingAsync(stoppingToken);
                await new MvpService(db).CloseDeliveredOutsideReturnWindowAsync(stoppingToken);
                var pending = await db.NotificationOutbox.Where(x => x.Status == "Pending")
                    .OrderBy(x => x.Id).Take(20).ToListAsync(stoppingToken);
                foreach (var mail in pending)
                {
                    try
                    {
                        var host = config["Mail:Host"];
                        if (!string.IsNullOrWhiteSpace(host))
                        {
                            using var smtp = new SmtpClient(host, int.TryParse(config["Mail:Port"], out var port) ? port : 1025);
                            using var message = new MailMessage("g4-demo@example.test", mail.Recipient, mail.Subject, mail.Body);
                            await smtp.SendMailAsync(message, stoppingToken);
                            mail.Status = "Sent";
                        }
                        else mail.Status = "Captured";
                        mail.SentAt = DateTime.UtcNow;
                    }
                    catch (SmtpException ex)
                    {
                        mail.Attempts++;
                        logger.LogWarning(ex, "Demo email send failed for notification {NotificationId}", mail.Id);
                    }
                }
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Demo maintenance cycle failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
