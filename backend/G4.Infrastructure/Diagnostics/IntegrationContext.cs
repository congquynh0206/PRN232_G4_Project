namespace G4.Infrastructure.Diagnostics;
public sealed record IntegrationCallContext(int? OrderId, string CorrelationId, int Attempt=1, int? NotificationId=null, int? Cycle=null);
public static class IntegrationContext
{
    private static readonly AsyncLocal<IntegrationCallContext?> Context = new();
    public static IntegrationCallContext Current => Context.Value ?? new(null, Guid.NewGuid().ToString("N"));
    public static IDisposable Begin(int? orderId,string correlationId,int attempt=1,int? notificationId=null,int? cycle=null)
    {
        var previous=Context.Value;
        Context.Value=new(orderId,correlationId.Length<=64 ? correlationId : Guid.NewGuid().ToString("N"),attempt,notificationId,cycle);
        return new Restore(previous);
    }
    public static IDisposable ForOrder(int? orderId,int attempt=1)
    { var current=Current; return Begin(orderId,current.CorrelationId,attempt,current.NotificationId,current.Cycle); }
    private sealed class Restore(IntegrationCallContext? previous):IDisposable { public void Dispose()=>Context.Value=previous; }
}
