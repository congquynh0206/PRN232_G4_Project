using System.Diagnostics;
using System.Text.RegularExpressions;
namespace G4.Infrastructure.Diagnostics;
public sealed class IntegrationCallRecorder(IIntegrationLogWriter? writer=null)
{
    public IntegrationAttempt Start(string service,string operation,string mode,string? entityId=null) => new(writer,IntegrationContext.Current,service,operation,mode,entityId);
}
public sealed class IntegrationAttempt(IIntegrationLogWriter? writer,IntegrationCallContext context,string service,string operation,string mode,string? entityId) : IAsyncDisposable
{
    private readonly Stopwatch timer=Stopwatch.StartNew();
    private int completed;
    public int? HttpStatus { get; set; }
    public async Task CompleteAsync(string outcome,int? httpStatus=null,string? providerReference=null,string? errorCode=null,string? safeSummary=null,CancellationToken ct=default)
    {
        if(Interlocked.Exchange(ref completed,1)!=0 || writer==null)return;
        try { await writer.WriteAsync(new(context.OrderId,context.CorrelationId,service,operation,mode,Reference(entityId,100),
            context.Attempt,outcome,httpStatus ?? HttpStatus,timer.ElapsedMilliseconds,Reference(providerReference,200),errorCode,safeSummary,
            context.NotificationId,context.Cycle,DateTime.UtcNow),CancellationToken.None); }
        catch(Exception) { /* Diagnostics cannot change provider results. */ }
    }
    public Task FailAsync(Exception ex,CancellationToken ct=default)
    {
        int? exceptionStatus=(ex as HttpRequestException)?.StatusCode is { } value ? (int)value : null;
        var status=HttpStatus ?? exceptionStatus;
        var ambiguousMoneyResponse=service=="PayPal" && operation is "Capture" or "Refund" &&
            (status is >=200 and <300 || status>=500);
        var unknown=ex is OperationCanceledException || ex is HttpRequestException && status==null || ambiguousMoneyResponse;
        return CompleteAsync(unknown ? "Unknown" : "Failed",status,errorCode:unknown ? "ResultUnknown" : status>=400 ? $"HTTP_{status}" : "ProviderResponseInvalid",
            safeSummary:unknown ? "Chưa xác định kết quả từ dịch vụ; cần kiểm tra trạng thái giao dịch." : "Dịch vụ chưa hoàn tất thao tác hoặc phản hồi chưa hợp lệ.",ct:ct);
    }
    public ValueTask DisposeAsync() => new(CompleteAsync("Unknown",errorCode:"Interrupted",safeSummary:"Thao tác bị gián đoạn; chưa xác định kết quả."));
    private static string? Reference(string? value,int max) => value!=null && value.Length<=max && Regex.IsMatch(value,"^[A-Za-z0-9_.:-]+$") ? value : null;
}
