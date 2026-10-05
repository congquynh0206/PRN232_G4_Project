using System.Net;
using System.Net.Mail;
using G4.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace G4.Infrastructure.Notifications;

public sealed class CommerceMaintenanceWorker(IServiceScopeFactory scopes, ILogger<CommerceMaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var verifyingOrderIds = await db.Payments.Where(p => p.Method == "PayPal" && p.Status == "Verifying")
                    .Select(p => p.OrderId).Distinct().OrderBy(x => x).Take(20).ToListAsync(stoppingToken);
                var paypal = scope.ServiceProvider.GetRequiredService<IPayPalPaymentService>();
                foreach (var orderId in verifyingOrderIds)
                {
                    if (orderId is null) continue;
                    try { await paypal.ReconcileAsync(orderId.Value, stoppingToken); }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                    {
                        logger.LogWarning(ex, "PayPal reconciliation pending for order {OrderId}", orderId);
                    }
                }
                var checkout = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
                await checkout.ExpirePendingAsync(stoppingToken);
                await checkout.CloseDeliveredOutsideReturnWindowAsync(stoppingToken);
                var finance = scope.ServiceProvider.GetRequiredService<ISellerFinanceService>();
                await finance.BackfillAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<IReturnService>().MaintainAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<IDisputeService>().MaintainAsync(stoppingToken);
                var pendingDisputeRefunds = await db.SellerSettlements.Where(x => x.Status == "RefundPending" &&
                    !db.Disputes.Any(d => d.OrderId == x.OrderId && d.WorkflowEnabled))
                    .Select(x => x.OrderId).OrderBy(x => x).Take(20).ToListAsync(stoppingToken);
                var returns = scope.ServiceProvider.GetRequiredService<IReturnService>();
                foreach (var orderId in pendingDisputeRefunds)
                {
                    try { await returns.RefundAsync(orderId, "dispute", null, stoppingToken); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(ex, "Dispute refund pending for order {OrderId}", orderId);
                    }
                }
                await finance.ReleaseDueFundsAsync(stoppingToken);
                await finance.AdvancePayoutsAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<NotificationProcessor>().ProcessDueAsync(20, stoppingToken);
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Commerce maintenance cycle failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
