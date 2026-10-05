using G4.Infrastructure.Diagnostics;
using G4.Infrastructure.Integrations.PayPal;
using G4.Application.Integrations;
using Microsoft.Extensions.Configuration;
using System.Net;

public static class IntegrationDiagnosticsChecks
{
    public static async Task RunAsync()
    {
        var writer=new Writer();
        using(var scope=IntegrationContext.Begin(17,"test-request"))
        {
            await using(var call=new IntegrationCallRecorder(writer).Start("PayPal","Capture","Sandbox"))
                await call.FailAsync(new OperationCanceledException("secret-canary"));
            using(var nested=IntegrationContext.Begin(19,"nested-request")) { if(IntegrationContext.Current.OrderId!=19)throw new Exception("Nested log context lost"); }
            if(IntegrationContext.Current.OrderId!=17)throw new Exception("Nested log context not restored");
        }
        if(writer.Entries.Single().Outcome!="Unknown" || writer.Entries.Any(x=>(x.ErrorSummary??"").Contains("secret-canary"))) throw new Exception("Timeout or log redaction incorrect");
        var parallel=await Task.WhenAll(new[]{101,102}.Select(async id=>{using var context=IntegrationContext.Begin(id,"parallel-"+id);await Task.Yield();return IntegrationContext.Current.OrderId;}));
        if(!parallel.SequenceEqual(new int?[]{101,102}))throw new Exception("Parallel requests mixed diagnostic order context");
        writer.Entries.Clear();
        var http=new HttpClient(new Handler());
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["PayPal:ClientId"]="client",["PayPal:ClientSecret"]="secret-canary" }).Build();
        using(var scope=IntegrationContext.Begin(17,"paypal-request"))
        {
            var created=await new PayPalSandboxGateway(http,config,writer).CreateAsync(20m,"USD","key","http://localhost/return","http://localhost/cancel",default);
            if(created.Id!="PP-17" || writer.Entries.Count!=2 || writer.Entries[0].Operation!="OAuth" || writer.Entries[1].Operation!="CreateOrder" || writer.Entries.Any(x=>x.OrderId!=17)) throw new Exception("Gateway actual calls not logged");
        }
        var successful=await new PayPalSandboxGateway(http,config,new ThrowingWriter()).CreateAsync(20m,"USD","key","http://localhost/return","http://localhost/cancel",default);
        if(successful.Id!="PP-17")throw new Exception("Log failure changed successful gateway result");
        writer.Entries.Clear();
        var malformed=new PayPalSandboxGateway(new HttpClient(new MalformedHandler()),config,writer);
        try { await malformed.RefundAsync("CAP-17",20m,"USD","refund-key",default);throw new Exception("Invalid refund accepted"); }catch(System.Text.Json.JsonException) { }
        if(writer.Entries.Last().Outcome!="Unknown")throw new Exception("Malformed 2xx refund falsely treated as certain failure");
        writer.Entries.Clear();
        var handler=new MoneyHandler();var gateway=new PayPalSandboxGateway(new HttpClient(handler),config,writer);
        var captured=await gateway.CaptureAsync("PP-17","capture-key",default);
        var queried=await gateway.GetAsync("PP-17",default);
        var refunded=await gateway.RefundAsync("CAP-17",20m,"USD","refund-key",default);
        if(captured.CaptureId!="CAP-17"||queried.Amount!=20m||refunded!="RF-17"||handler.Calls!=6||writer.Entries.Count!=6||
            string.Join(',',writer.Entries.Select(x=>x.Operation))!="OAuth,Capture,OAuth,QueryOrder,OAuth,Refund")throw new Exception("Actual Capture/Query/Refund calls or references lost");
        writer.Entries.Clear();handler.TimeoutCapture=true;
        try{await gateway.CaptureAsync("PP-17","timeout-key",default);throw new Exception("Provider timeout accepted");}catch(OperationCanceledException){ }
        await gateway.GetAsync("PP-17",default);
        if(writer.Entries.Count!=4||writer.Entries[1].Outcome!="Unknown"||writer.Entries[3].Outcome!="Succeeded")throw new Exception("Capture timeout and later query results conflated");
        if(System.Text.Json.JsonSerializer.Serialize(writer.Entries).Contains("secret-canary"))throw new Exception("Credentials leaked into log");
        writer.Entries.Clear();handler.TimeoutCapture=false;handler.IncompleteCapture=true;
        try { await gateway.CaptureAsync("PP-17","incomplete-key",default);throw new Exception("Incomplete COMPLETED capture accepted"); }
        catch(HttpRequestException) { }
        if(writer.Entries.Last().Outcome!="Unknown")throw new Exception("Incomplete completed response claimed capture success");
        Console.WriteLine("Integration correlation, timeout/redaction, PayPal call logging and failure isolation passed");
    }
    public sealed class Writer:IIntegrationLogWriter
    {
        public List<IntegrationLogEntry> Entries=new();
        public Task WriteAsync(IntegrationLogEntry entry,CancellationToken ct) { Entries.Add(entry); return Task.CompletedTask; }
    }
    private sealed class ThrowingWriter:IIntegrationLogWriter { public Task WriteAsync(IntegrationLogEntry e,CancellationToken ct)=>throw new Exception("secret-canary"); }
    private sealed class Handler:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content=new StringContent(req.RequestUri!.AbsolutePath.Contains("oauth") ? "{\"access_token\":\"secret-canary\"}" : "{\"id\":\"PP-17\",\"links\":[{\"rel\":\"approve\",\"href\":\"https://www.sandbox.paypal.com/approve\"}]}" ) });
    }
    private sealed class MalformedHandler:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(req.RequestUri!.AbsolutePath.Contains("oauth") ? "{\"access_token\":\"secret-canary\"}" : "{secret-canary") });
    }
    private sealed class MoneyHandler:HttpMessageHandler
    {
        public int Calls;public bool TimeoutCapture;public bool IncompleteCapture;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            Calls++;var path=req.RequestUri!.AbsolutePath;
            if(path.EndsWith("/capture")&&TimeoutCapture)throw new OperationCanceledException("secret-canary");
            if(path.EndsWith("/capture")&&IncompleteCapture)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"status\":\"COMPLETED\"}")});
            var json=path.Contains("oauth")?"{\"access_token\":\"secret-canary\"}":path.EndsWith("/refund")?"{\"id\":\"RF-17\",\"status\":\"COMPLETED\"}":"{\"status\":\"COMPLETED\",\"purchase_units\":[{\"payments\":{\"captures\":[{\"id\":\"CAP-17\",\"amount\":{\"value\":\"20.00\",\"currency_code\":\"USD\"}}]}}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
        }
    }
}
