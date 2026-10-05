using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using G4.Infrastructure.Diagnostics;

namespace G4.Infrastructure.Integrations.PayPal;

public sealed class PayPalSandboxGateway(HttpClient http, IConfiguration configuration, IIntegrationLogWriter? logs=null) : IPayPalGateway
{
    private readonly string clientId=configuration["PayPal:ClientId"] ?? "";
    private readonly string clientSecret=configuration["PayPal:ClientSecret"] ?? "";
    private readonly string baseUrl=configuration["PayPal:BaseUrl"] ?? "https://api-m.sandbox.paypal.com";

    public Task<PayPalCreated> CreateAsync(decimal amount,string currency,string requestId,string returnUrl,string cancelUrl,CancellationToken ct)
    {
        var body=new {
            intent="CAPTURE", purchase_units=new[] { new { amount=new { currency_code=currency,value=amount.ToString("F2",CultureInfo.InvariantCulture) } } },
            payment_source=new { paypal=new { experience_context=new { return_url=returnUrl,cancel_url=cancelUrl,user_action="PAY_NOW" } } }
        };
        return SendAsync(HttpMethod.Post,"/v2/checkout/orders","CreateOrder",requestId,body,ParseCreated,x=>x.Id,ct);
    }
    public Task<PayPalCaptured> CaptureAsync(string orderId,string requestId,CancellationToken ct) =>
        SendAsync(HttpMethod.Post,$"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}/capture","Capture",requestId,new {},ParseCapture,x=>x.CaptureId,ct);
    public Task<PayPalCaptured> GetAsync(string orderId,CancellationToken ct) =>
        SendAsync(HttpMethod.Get,$"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}","QueryOrder",null,null,ParseCapture,x=>x.CaptureId,ct);
    public Task<string> RefundAsync(string captureId,decimal amount,string currency,string requestId,CancellationToken ct) =>
        SendAsync(HttpMethod.Post,$"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund","Refund",requestId,
            new { amount=new { currency_code=currency,value=amount.ToString("F2",CultureInfo.InvariantCulture) } },ParseRefund,x=>x,ct);

    private async Task<T> SendAsync<T>(HttpMethod method,string path,string operation,string? requestId,object? body,
        Func<JsonElement,T> parse,Func<T,string?> reference,CancellationToken ct)
    {
        var token=await AccessTokenAsync(ct);
        using var request=new HttpRequestMessage(method,baseUrl.TrimEnd('/')+path);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        request.Headers.TryAddWithoutValidation("Prefer","return=representation");
        if(requestId!=null)request.Headers.TryAddWithoutValidation("PayPal-Request-Id",requestId);
        if(body!=null)request.Content=JsonContent.Create(body);
        return await RequestAsync(request,operation,parse,reference,ct);
    }
    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(clientId)||string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("PayPal sandbox credentials are not configured");
        using var request=new HttpRequestMessage(HttpMethod.Post,baseUrl.TrimEnd('/')+"/v1/oauth2/token");
        request.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(clientId+":"+clientSecret)));
        request.Content=new FormUrlEncodedContent(new Dictionary<string,string> { ["grant_type"]="client_credentials" });
        return await RequestAsync(request,"OAuth",root=>root.GetProperty("access_token").GetString() ?? throw new HttpRequestException("PayPal token missing"),_=>null,ct);
    }
    private async Task<T> RequestAsync<T>(HttpRequestMessage request,string operation,Func<JsonElement,T> parse,Func<T,string?> reference,CancellationToken ct)
    {
        await using var call=new IntegrationCallRecorder(logs).Start("PayPal",operation,"Sandbox");
        try
        {
            using var response=await http.SendAsync(request,ct);
            call.HttpStatus=(int)response.StatusCode;
            if(!response.IsSuccessStatusCode)throw new HttpRequestException($"PayPal API returned {response.StatusCode}",null,response.StatusCode);
            using var json=await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct),cancellationToken:ct);
            var result=parse(json.RootElement);
            if(result is PayPalCaptured capture && capture.Status=="COMPLETED" &&
                (string.IsNullOrWhiteSpace(capture.CaptureId)||capture.Amount<=0||string.IsNullOrWhiteSpace(capture.Currency)))
                throw new HttpRequestException("Completed PayPal response lacks capture details");
            await call.CompleteAsync("Succeeded",providerReference:reference(result),ct:ct);
            return result;
        }
        catch(Exception ex) { await call.FailAsync(ex,ct); throw; }
    }
    private static PayPalCreated ParseCreated(JsonElement root)
    {
        var id=root.GetProperty("id").GetString() ?? throw new HttpRequestException("PayPal order ID missing");
        var approve=root.GetProperty("links").EnumerateArray().FirstOrDefault(x=>x.GetProperty("rel").GetString() is "approve" or "payer-action");
        var url=approve.ValueKind==JsonValueKind.Undefined ? null : approve.GetProperty("href").GetString();
        return new(id,url ?? throw new HttpRequestException("PayPal approval URL missing"));
    }
    private static string ParseRefund(JsonElement root)
    {
        if(root.GetProperty("status").GetString()!="COMPLETED")throw new HttpRequestException("PayPal refund is not completed");
        return root.GetProperty("id").GetString() ?? throw new HttpRequestException("PayPal refund ID missing");
    }
    private static PayPalCaptured ParseCapture(JsonElement root)
    {
        var status=root.GetProperty("status").GetString() ?? "UNKNOWN";
        if(!root.TryGetProperty("purchase_units",out var units))return new(status,null,0m,"");
        var unit=units.EnumerateArray().First();
        if(!unit.TryGetProperty("payments",out var payments)||!payments.TryGetProperty("captures",out var captures))return new(status,null,0m,"");
        var capture=captures.EnumerateArray().FirstOrDefault();
        if(capture.ValueKind==JsonValueKind.Undefined)return new(status,null,0m,"");
        var money=capture.GetProperty("amount");
        return new(status,capture.GetProperty("id").GetString(),decimal.Parse(money.GetProperty("value").GetString()!,CultureInfo.InvariantCulture),
            money.GetProperty("currency_code").GetString() ?? "");
    }
}
