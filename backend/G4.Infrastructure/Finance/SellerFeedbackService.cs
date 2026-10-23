using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Finance;

public sealed class SellerFeedbackService(ApplicationDbContext db, TimeProvider clock)
    : ISellerFeedbackService
{
    public async Task<SellerFeedbackView> SubmitAsync(
        int orderId,
        int buyerId,
        SellerFeedbackInput input,
        CancellationToken ct = default
    )
    {
        var comment = (input.Comment ?? "").Trim();
        var key = input.Key?.Trim();
        if (
            input.Rating is < 1 or > 5
            || comment.Length > 2000
            || string.IsNullOrWhiteSpace(key)
            || key.Length > 100
        )
            throw new ArgumentException(
                "Chọn từ 1 đến 5 sao, nhận xét tối đa 2.000 ký tự và mã yêu cầu hợp lệ."
            );
        await using var tx =
            db.Database.IsRelational() && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable,
                    ct
                )
                : null;
        if (db.Database.IsSqlServer())
        {
            var resource = $"commerce:buyer-feedback:{buyerId}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"DECLARE @r int;
                EXEC @r = sys.sp_getapplock @Resource={resource}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000;
                IF @r < 0 THROW 51000, 'Cannot acquire buyer feedback lock', 1;",
                ct
            );
            await db
                .OrderTables.FromSqlInterpolated(
                    $"SELECT * FROM [OrderTable] WITH (UPDLOCK,HOLDLOCK) WHERE [id]={orderId}"
                )
                .AsNoTracking()
                .SingleAsync(ct);
        }
        var order =
            await db.OrderTables.AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy đơn hàng.");
        if (order.BuyerId != buyerId || order.SellerId == buyerId)
            throw new UnauthorizedAccessException("Bạn không được đánh giá đơn này.");
        var existing = await db
            .SellerOrderFeedbacks.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrderId == orderId || x.BuyerId == buyerId && x.RequestKey == key,
                ct
            );
        if (existing != null)
        {
            if (
                existing.OrderId != orderId
                || existing.RequestKey != key
                || existing.Rating != input.Rating
                || existing.Comment != comment
            )
                throw new InvalidOperationException(
                    "Đơn đã được đánh giá hoặc mã yêu cầu đã dùng với nội dung khác."
                );
            if (tx != null)
                await tx.CommitAsync(ct);
            return View(existing);
        }
        if (
            order.SellerId == null
            || order.Status is not ("Delivered" or "Closed")
            || !await db.Payments.AnyAsync(x => x.OrderId == orderId && x.Status == "Succeeded", ct)
        )
            throw new InvalidOperationException(
                "Chỉ được đánh giá đơn đã thanh toán và giao thành công hoặc hoàn tất."
            );
        var feedback = new SellerOrderFeedback
        {
            OrderId = orderId,
            BuyerId = buyerId,
            SellerId = order.SellerId.Value,
            Rating = input.Rating,
            Comment = comment,
            RequestKey = key!,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.SellerOrderFeedbacks.Add(feedback);
        await db.SaveChangesAsync(ct);
        if (tx != null)
            await tx.CommitAsync(ct);
        return View(feedback);
    }

    public static SellerFeedbackView View(SellerOrderFeedback x) =>
        new(x.Id, x.OrderId, x.Rating, x.Comment, x.CreatedAt);
}
