using System.Data;
using System.Text.Json;
using G4.Contracts.Checkout;
using G4.Infrastructure.Promotions;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Checkout;

public sealed partial class ImmediateCheckoutService
{
    public async Task<ImmediatePaymentAttempt> PayCardAsync(
        int buyerId, Guid id, CardPaymentRequest input, CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(input.Key) || input.Key.Length > 100)
            throw new ArgumentException("Payment key is required (maximum 100 characters).");
        long attemptId;
        await using (var tx = await BeginAsync(ct))
        {
            var session = await db.ImmediateCheckouts
                .SingleOrDefaultAsync(s => s.Id == id && s.BuyerId == buyerId, ct)
                ?? throw new KeyNotFoundException("Checkout not found");
            await ReloadAsync(session, ct);
            var previous = await db.ImmediatePaymentAttempts
                .SingleOrDefaultAsync(a => a.Key == input.Key, ct);
            if (previous != null && previous.CheckoutId != id)
                throw new InvalidOperationException("Payment key belongs to another checkout.");
            var active = await db.ImmediatePaymentAttempts
                .Where(a => a.CheckoutId == id
                    && (a.Status == "Pending" || a.Status == "Verifying" || a.Status == "Succeeded"))
                .OrderByDescending(a => a.Id).FirstOrDefaultAsync(ct);
            if (previous != null || active != null)
            {
                attemptId = (previous ?? active)!.Id;
            }
            else
            {
                if (session.ExpiresAt <= DateTime.UtcNow)
                    throw new InvalidOperationException("Checkout expired. Request a new quote.");
                var request = Request(session);
                await ValidateCurrentQuoteAsync(buyerId, request, ct);
                var attempt = new ImmediatePaymentAttempt
                {
                    CheckoutId = id,
                    Key = input.Key,
                    Amount = request.ExpectedTotal!.Value,
                    AuthorizationOutcome = CardPaymentSimulator.Outcome(input.Number, input.Expiry),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                db.ImmediatePaymentAttempts.Add(attempt);
                session.Status = "Verifying";
                await db.SaveChangesAsync(ct);
                attemptId = attempt.Id;
            }
            if (tx != null)
                await tx.CommitAsync(ct);
        }
        return await ProcessAsync(attemptId, ct);
    }

    public async Task<ImmediatePaymentAttempt> ReconcileAsync(
        int buyerId, Guid id, CancellationToken ct = default
    )
    {
        if (!await db.ImmediateCheckouts.AnyAsync(s => s.Id == id && s.BuyerId == buyerId, ct))
            throw new KeyNotFoundException("Checkout not found");
        var attempt = await db.ImmediatePaymentAttempts.AsNoTracking()
            .Where(a => a.CheckoutId == id).OrderByDescending(a => a.Id).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No payment attempt exists.");
        return await ProcessAsync(attempt.Id, ct);
    }

    public async Task MaintainAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var ids = await db.ImmediatePaymentAttempts.AsNoTracking()
            .Where(a => (a.Status == "Pending" || a.Status == "Verifying")
                && (a.ProcessingUntil == null || a.ProcessingUntil <= now))
            .OrderBy(a => a.UpdatedAt).Select(a => a.Id).Take(20).ToListAsync(ct);
        foreach (var id in ids)
            await ProcessAsync(id, ct);
    }

    private async Task<ImmediatePaymentAttempt> ProcessAsync(long id, CancellationToken ct)
    {
        var token = Guid.NewGuid();
        var acquired = false;
        await MutateAsync(id, null, (attempt, _) =>
        {
            if (Terminal(attempt) || attempt.ProcessingUntil > DateTime.UtcNow)
                return Task.CompletedTask;
            attempt.ProcessingToken = token;
            attempt.ProcessingUntil = DateTime.UtcNow.AddMinutes(2);
            acquired = true;
            return Task.CompletedTask;
        }, ct);
        if (!acquired)
            return await ReadAsync(id, ct);

        try
        {
            for (var step = 0; step < 12; step++)
            {
                var attempt = await ReadAsync(id, ct);
                if (Terminal(attempt))
                    break;
                var key = "IP-" + id;
                if (attempt.Stage == "Authorizing")
                {
                    // Always query before retrying an uncertain provider command.
                    var result = await gateway.GetAsync(key, ct);
                    if (result.Status == "NotFound")
                        result = await gateway.AuthorizeAsync(
                            key, attempt.Amount, attempt.AuthorizationOutcome, ct);
                    await AcceptProviderStateAsync(id, token, result, ct);
                }
                else if (attempt.Stage == "Authorized")
                {
                    await ClaimInventoryAsync(id, token, ct);
                }
                else if (attempt.Stage == "Capturing")
                {
                    var result = await gateway.GetAsync(key, ct);
                    if (result.Status == "Authorized")
                        result = await gateway.CaptureAsync(key, ct);
                    await AcceptProviderStateAsync(id, token, result, ct);
                }
                else if (attempt.Stage == "Captured")
                {
                    await FinalizeAsync(id, token, ct);
                }
                else if (attempt.Stage is "Voiding" or "Refunding")
                {
                    var result = await gateway.GetAsync(key, ct);
                    if (result.Status is "Authorized" or "CaptureFailed")
                        result = await gateway.VoidAsync(key, ct);
                    else if (result.Status is "Captured" or "PartiallyRefunded")
                        result = await gateway.RefundAsync(key, ct);
                    await AcceptProviderStateAsync(id, token, result, ct);
                }
                else
                {
                    throw new InvalidOperationException("Unknown immediate payment stage");
                }
                var current = await ReadAsync(id, ct);
                if (current.Stage == attempt.Stage && current.Status == "Verifying")
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Provider errors and database errors do not imply a failed charge.
            // Persisted phase/command key survives; the worker queries the provider.
            db.ChangeTracker.Clear();
            await MutateAsync(id, token, (attempt, _) =>
            {
                if (!Terminal(attempt))
                {
                    attempt.Status = "Verifying";
                    attempt.ErrorCode ??= "ReconciliationRequired";
                }
                return Task.CompletedTask;
            }, ct, ignoreLostLease: true);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                await MutateAsync(id, token, (attempt, _) =>
                {
                    attempt.ProcessingToken = null;
                    attempt.ProcessingUntil = null;
                    return Task.CompletedTask;
                }, ct, ignoreLostLease: true);
            }
        }
        return await ReadAsync(id, ct);
    }

    private async Task ClaimInventoryAsync(long id, Guid token, CancellationToken ct)
    {
        await MutateAsync(id, token, async (attempt, session) =>
        {
            var request = Request(session);
            try
            {
                await ValidateCurrentQuoteAsync(session.BuyerId, request, ct);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                attempt.Stage = "Voiding";
                attempt.ErrorCode = ex.Message.Contains("stock", StringComparison.OrdinalIgnoreCase)
                    ? "OutOfStock" : "CheckoutChanged";
                return;
            }
            var line = request.Items.Single();
            if (db.Database.IsRelational())
            {
                var changed = await db.Inventories
                    .Where(i => i.ProductId == line.ProductId && i.Quantity >= line.Quantity)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(i => i.Quantity, i => i.Quantity - line.Quantity)
                        .SetProperty(i => i.LastUpdated, DateTime.UtcNow), ct);
                if (changed > 1)
                    throw new DbUpdateException("Multiple inventory rows for the same product");
                if (changed != 1)
                {
                    attempt.Stage = "Voiding";
                    attempt.ErrorCode = "OutOfStock";
                    return;
                }
            }
            else
            {
                var stock = await db.Inventories.SingleAsync(i => i.ProductId == line.ProductId, ct);
                if (stock.Quantity < line.Quantity)
                {
                    attempt.Stage = "Voiding";
                    attempt.ErrorCode = "OutOfStock";
                    return;
                }
                stock.Quantity -= line.Quantity;
            }
            // A payment-time claim, never a checkout-time reservation. Unknown capture
            // retains the claim until reconciliation proves void/refund or succeeds.
            attempt.InventoryHeld = true;
            attempt.Stage = "Capturing";
        }, ct);
    }

    private async Task FinalizeAsync(long id, Guid token, CancellationToken ct)
    {
        await MutateAsync(id, token, (attempt, _) =>
        {
            attempt.FinalizationAttempts++;
            return Task.CompletedTask;
        }, ct);
        try
        {
            await MutateAsync(id, token, async (attempt, session) =>
            {
                if (attempt.FinalizationAttempts > 3)
                {
                    attempt.Stage = "Refunding";
                    attempt.ErrorCode = "OrderPersistenceFailed";
                    return;
                }
                var order = await checkout.CreateCapturedImmediateOrderAsync(
                    session.BuyerId, Request(session), attempt, ct);
                attempt.OrderId = session.OrderId = order.Id;
                attempt.InventoryHeld = false;
                attempt.Status = session.Status = "Succeeded";
                attempt.Stage = "Completed";
                attempt.ErrorCode = null;
            }, ct);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // The order transaction has rolled back before recording compensation.
            db.ChangeTracker.Clear();
            await MutateAsync(id, token, (attempt, _) =>
            {
                if (!Terminal(attempt))
                {
                    attempt.Stage = "Refunding";
                    attempt.ErrorCode = "CheckoutChanged";
                }
                return Task.CompletedTask;
            }, ct);
        }
    }

    private async Task AcceptProviderStateAsync(
        long id, Guid token, CardAuthorizationState result, CancellationToken ct
    )
    {
        await MutateAsync(id, token, async (attempt, session) =>
        {
            if (result.Status is "Authorized" or "Captured" && result.Amount != attempt.Amount)
            {
                attempt.Stage = result.Status == "Captured" ? "Refunding" : "Voiding";
                attempt.ErrorCode = "ProviderAmountMismatch";
                return;
            }
            attempt.AuthorizationId = result.AuthorizationId ?? attempt.AuthorizationId;
            attempt.CaptureId = result.CaptureId ?? attempt.CaptureId;
            if (result.Status == "Authorized" && attempt.Stage == "Authorizing")
                attempt.Stage = "Authorized";
            else if (result.Status == "Captured")
            {
                // An unexpected capture without inventory can never create a paid order.
                attempt.Stage = attempt.InventoryHeld && attempt.Stage != "Voiding"
                    && attempt.Stage != "Refunding" ? "Captured" : "Refunding";
            }
            else if (result.Status == "PartiallyRefunded")
            {
                attempt.Stage = "Refunding";
                attempt.ErrorCode ??= "UnexpectedRefund";
            }
            else if (result.Status is "Declined" or "Voided" or "Refunded")
            {
                if (attempt.InventoryHeld)
                {
                    var line = Request(session).Items.Single();
                    if (db.Database.IsRelational())
                    {
                        var changed = await db.Inventories.Where(i => i.ProductId == line.ProductId)
                            .ExecuteUpdateAsync(update => update
                                .SetProperty(i => i.Quantity, i => i.Quantity + line.Quantity), ct);
                        if (changed != 1)
                            throw new InvalidOperationException("Inventory restore failed");
                    }
                    else
                    {
                        var stock = await db.Inventories.SingleAsync(
                            i => i.ProductId == line.ProductId, ct);
                        stock.Quantity += line.Quantity;
                    }
                    attempt.InventoryHeld = false;
                }
                attempt.Stage = result.Status;
                attempt.Status = "Failed";
                attempt.ErrorCode ??= result.Status == "Declined"
                    ? attempt.AuthorizationOutcome : result.Status;
                session.Status = "Ready";
            }
            else if (result.Status == "CaptureFailed")
            {
                attempt.Stage = "Voiding";
                attempt.ErrorCode = "CaptureFailed";
            }
            else
            {
                attempt.Status = "Verifying";
            }
        }, ct);
    }

    private async Task ValidateCurrentQuoteAsync(
        int buyerId, CheckoutRequest request, CancellationToken ct
    )
    {
        var product = await db.Products.AsNoTracking()
            .SingleAsync(p => p.Id == request.Items.Single().ProductId, ct);
        if (!product.IsListed || !product.ImmediatePaymentRequired)
            throw new InvalidOperationException("Product payment configuration has changed.");
        var quote = await checkout.QuoteAsync(buyerId, request, ct);
        if (quote.Total != request.ExpectedTotal
            || quote.PricingFingerprint != request.PricingFingerprint)
            throw new InvalidOperationException("Price changed. Review a new checkout quote.");
    }

    private async Task MutateAsync(
        long id, Guid? token,
        Func<ImmediatePaymentAttempt, ImmediateCheckout, Task> change,
        CancellationToken ct, bool ignoreLostLease = false
    )
    {
        await using var tx = await BeginAsync(ct);
        var attempt = await db.ImmediatePaymentAttempts.SingleAsync(a => a.Id == id, ct);
        await ReloadAsync(attempt, ct);
        if (token != null && (attempt.ProcessingToken != token
            || attempt.ProcessingUntil <= DateTime.UtcNow))
        {
            if (ignoreLostLease)
                return;
            throw new InvalidOperationException("Payment processing lease was superseded.");
        }
        var session = await db.ImmediateCheckouts.SingleAsync(s => s.Id == attempt.CheckoutId, ct);
        await ReloadAsync(session, ct);
        await change(attempt, session);
        attempt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (tx != null)
            await tx.CommitAsync(ct);
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAsync(
        CancellationToken ct
    )
    {
        if (!db.Database.IsRelational())
            return null;
        var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await PromotionLifecycle.LockReservationsAsync(db, ct);
            return tx;
        }
        catch
        {
            await tx.DisposeAsync();
            throw;
        }
    }

    private Task ReloadAsync(object entity, CancellationToken ct) =>
        db.Database.IsRelational() ? db.Entry(entity).ReloadAsync(ct) : Task.CompletedTask;

    private Task<ImmediatePaymentAttempt> ReadAsync(long id, CancellationToken ct) =>
        db.ImmediatePaymentAttempts.AsNoTracking().SingleAsync(a => a.Id == id, ct);

    private static CheckoutRequest Request(ImmediateCheckout session) =>
        JsonSerializer.Deserialize<CheckoutRequest>(session.RequestJson)!;

    private static bool Terminal(ImmediatePaymentAttempt attempt) =>
        attempt.Status is "Succeeded" or "Failed";
}
