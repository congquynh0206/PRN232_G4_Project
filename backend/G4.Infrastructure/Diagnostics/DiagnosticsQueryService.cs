using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace G4.Infrastructure.Diagnostics;

public sealed class DiagnosticsQueryService(ApplicationDbContext db)
{
    private static readonly Expression<Func<NotificationOutbox,EmailNotificationView>> MailView = x=>new(x.Id,x.OrderId,x.EventType,x.Recipient,
        x.From,x.Subject,x.Status,x.Attempts,x.AttemptsInCycle,x.Cycle,x.CreatedAt,x.LastAttemptAt,x.NextAttemptAt,x.SentAt,x.CapturedAt,x.LastErrorCode,x.LastErrorSummary);
    private static readonly Expression<Func<IntegrationLog,IntegrationLogView>> LogView = x=>new(x.Id,x.OrderId,x.CorrelationId,x.Service,x.Operation,x.Mode,
        x.EntityId,x.Attempt,x.Outcome,x.HttpStatus,x.DurationMs,x.ProviderReference,x.ErrorCode,x.ErrorSummary,x.NotificationId,x.Cycle,x.CreatedAt);

    public async Task<DiagnosticsPage<EmailNotificationView>> NotificationsAsync(int actorId,string role,NotificationQuery options,CancellationToken ct)
    {
        ValidatePage(options.Page,options.PageSize,options.OrderId);
        ValidateFilter(options.EventType,["PaymentSucceeded","Delivered","DeliveryFailed","Refunded"]);
        ValidateFilter(options.Status,["Pending","Processing","Sent","Captured","Failed"]);
        var query=VisibleNotifications(actorId,role);
        if(options.OrderId!=null)query=query.Where(x=>x.OrderId==options.OrderId);
        if(!string.IsNullOrEmpty(options.EventType))query=query.Where(x=>x.EventType==options.EventType);
        if(!string.IsNullOrEmpty(options.Status))query=query.Where(x=>x.Status==options.Status);
        return await PageAsync(query.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id),MailView,options.Page,options.PageSize,ct);
    }

    public async Task<EmailNotificationDetail> NotificationAsync(int id,int actorId,string role,CancellationToken ct)
    {
        if(id<=0)throw new ArgumentException("Mã thông báo chưa hợp lệ.");
        var mail=await VisibleNotifications(actorId,role).SingleOrDefaultAsync(x=>x.Id==id,ct) ?? throw new KeyNotFoundException("Không tìm thấy thông báo.");
        var attempts=await db.IntegrationLogs.AsNoTracking().Where(x=>x.NotificationId==id && x.Service=="Email")
            .OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Select(LogView).ToListAsync(ct);
        return new(MailView.Compile()(mail),mail.Body,mail.HtmlBody,attempts);
    }

    public async Task<DiagnosticsPage<IntegrationLogView>> LogsAsync(int actorId,string role,IntegrationLogQueryOptions options,CancellationToken ct)
    {
        if(role is not ("seller" or "admin"))throw new UnauthorizedAccessException("Tài khoản không có quyền xem nhật ký tích hợp.");
        ValidatePage(options.Page,options.PageSize,options.OrderId);
        ValidateFilter(options.Service,["PayPal","Carrier","Email","Card","Promotion"]);
        ValidateFilter(options.Outcome,["Succeeded","Failed","Unknown"]);
        if(options.From!=null && options.To!=null && options.From>=options.To)throw new ArgumentException("Khoảng thời gian chưa hợp lệ.");
        var query=db.IntegrationLogs.AsNoTracking();
        if(role!="admin")query=query.Where(x=>db.OrderTables.Any(o=>o.Id==x.OrderId && o.SellerId==actorId));
        if(options.OrderId!=null)query=query.Where(x=>x.OrderId==options.OrderId);
        if(!string.IsNullOrEmpty(options.Service))query=query.Where(x=>x.Service==options.Service);
        if(!string.IsNullOrEmpty(options.Outcome))query=query.Where(x=>x.Outcome==options.Outcome);
        if(options.From!=null)query=query.Where(x=>x.CreatedAt>=options.From);
        if(options.To!=null)query=query.Where(x=>x.CreatedAt<options.To);
        return await PageAsync(query.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id),LogView,options.Page,options.PageSize,ct);
    }

    private IQueryable<NotificationOutbox> VisibleNotifications(int actorId,string role)
    {
        if(role is not ("buyer" or "seller" or "admin"))throw new UnauthorizedAccessException("Tài khoản không có quyền xem email.");
        var query=db.NotificationOutbox.AsNoTracking();
        if(role=="buyer")return query.Where(x=>db.OrderTables.Any(o=>o.Id==x.OrderId && o.BuyerId==actorId));
        if(role=="seller")return query.Where(x=>db.OrderTables.Any(o=>o.Id==x.OrderId && o.SellerId==actorId));
        return query;
    }
    private static void ValidatePage(int page,int size,int? orderId)
    { if(page<1 || size is <1 or >100 || orderId<=0)throw new ArgumentException("Mã đơn hoặc phân trang chưa hợp lệ."); }
    private static void ValidateFilter(string? value,string[] allowed)
    { if(!string.IsNullOrEmpty(value) && !allowed.Contains(value))throw new ArgumentException("Bộ lọc chưa hợp lệ."); }
    private static async Task<DiagnosticsPage<TView>> PageAsync<T,TView>(IQueryable<T> query,Expression<Func<T,TView>> projection,int page,int size,CancellationToken ct)
    {
        var count=await query.CountAsync(ct); page=Math.Min(page,Math.Max(1,(count+size-1)/size));
        return new(page,size,count,await query.Skip((page-1)*size).Take(size).Select(projection).ToListAsync(ct));
    }
}
