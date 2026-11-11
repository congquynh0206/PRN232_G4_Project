using System.Net;
using System.Text;
using G4.Infrastructure.Integrations.VietMap;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

/// <summary>VietMap gateway against a fake HTTP handler; never calls the real provider.</summary>
internal static class VietMapChecks
{
    private const string Key = "test-vietmap-key";

    public static async Task RunAsync()
    {
        var handler = new Handler();
        var logs = new IntegrationDiagnosticsChecks.Writer();
        var gateway = Gateway(handler, Key, logs);
        Check(gateway.Enabled && !Gateway(handler, "", logs).Enabled, "enabled only with a key");

        // Shapes copied from live responses on 11/11/2026 (new two-level units).
        handler.Reply = (HttpStatusCode.OK, """
            [{"ref_id":"auto:abc","display":"197 Trần Phú, Phường Chợ Quán, Thành phố Hồ Chí Minh",
              "name":"197 Trần Phú","address":"Phường Chợ Quán, Thành phố Hồ Chí Minh",
              "boundaries":[
                {"type":2,"id":18700,"name":"Chợ Quán","prefix":"Phường","full_name":"Phường Chợ Quán"},
                {"type":0,"id":12,"name":"Hồ Chí Minh","prefix":"Thành phố",
                 "full_name":"Thành phố Hồ Chí Minh"}]},
             {"ref_id":"auto:nobounds","display":"12 Lê Lợi, Huế","name":"12 Lê Lợi"},
             {"ref_id":"","display":"ignored"}]
            """);
        var rows = await gateway.SuggestAsync(" 197 Trần Phú ", default);
        Check(
            rows.Count == 2 && rows[0].Ref == "auto:abc",
            "rows without ref_id are dropped"
        );
        var url = handler.Requests.Single();
        Check(
            url.StartsWith("https://maps.vietmap.vn/api/autocomplete/v4?")
                && url.Contains("display_type=1")
                && url.Contains("text=197%20Tr")
                && url.Contains("apikey=" + Key),
            "autocomplete v4 uses the new two-level format"
        );
        await gateway.SuggestAsync("197 trần phú", default);
        Check(handler.Requests.Count == 1, "repeated text is served from the cache");

        var picked = await gateway.DetailAsync("auto:abc", default);
        Check(
            handler.Requests.Count == 1
                && picked is
                {
                    Street: "197 Trần Phú",
                    City: "Phường Chợ Quán",
                    State: "Hồ Chí Minh",
                    Country: "Vietnam",
                },
            "a row with ward and province is used without a Place call"
        );

        handler.Reply = (HttpStatusCode.OK, """
            {"display":"12 Lê Lợi, Phường Thuận Hóa, Thành phố Huế","name":"12 Lê Lợi",
             "hs_num":"12","street":"Lê Lợi","address":"Phường Thuận Hóa, Thành phố Huế",
             "city":"Thành phố Huế","district":"","ward":"Phường Thuận Hóa"}
            """);
        var detail = await gateway.DetailAsync("auto:nobounds", default);
        Check(
            detail is
            {
                Street: "12 Lê Lợi",
                City: "Phường Thuận Hóa",
                State: "Huế",
                Country: "Vietnam",
            },
            "place v4 builds the street from hs_num + street, not the admin address"
        );
        Check(
            handler.Requests[^1].Contains("refid=auto%3Anobounds"),
            "refid is passed verbatim"
        );

        handler.Reply = (HttpStatusCode.Locked, "Your request is limited");
        await Unavailable(() => gateway.SuggestAsync("quota text", default), "423 quota");
        handler.Reply = (HttpStatusCode.OK, "not json");
        await Unavailable(() => gateway.SuggestAsync("bad json", default), "broken body");
        await Unavailable(
            () => Gateway(handler, "", logs).SuggestAsync("no key", default),
            "missing key"
        );
        Check(
            logs.Entries.Count >= 4
                && logs.Entries.All(e => e.Service == "VietMap")
                && logs.Entries.All(e =>
                    !(e.ErrorSummary ?? "").Contains(Key)
                    && !(e.ProviderReference ?? "").Contains(Key)
                    && !(e.EntityId ?? "").Contains(Key)
                )
                && logs.Entries.Any(e => e.ErrorCode == "VietMap423"),
            "calls are logged without the API key"
        );
        Console.WriteLine(
            "VietMap autocomplete/place mapping, cache, quota/error handling and key redaction passed"
        );
    }

    /// <summary>
    /// Opt-in (--vietmap-live): one autocomplete + one place call with the key in G4.Api
    /// user-secrets (VietMap:ApiKey). Uses 2 of the daily free transactions.
    /// </summary>
    public static async Task LiveAsync()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets("fd8343a1-846a-418e-82cd-10b3666b402b")
            .AddEnvironmentVariables()
            .Build();
        if (string.IsNullOrWhiteSpace(config["VietMap:ApiKey"]))
            throw new Exception("VietMap:ApiKey is not set in G4.Api user-secrets");
        var gateway = new VietMapGateway(
            new HttpClient(),
            config,
            new MemoryCache(new MemoryCacheOptions())
        );
        var rows = await gateway.SuggestAsync("197 Trần Phú", default);
        Check(rows.Count > 0, "live autocomplete returned rows");
        var detail = await gateway.DetailAsync(rows[0].Ref, default);
        Console.WriteLine($"Live suggestion: {rows[0].Display}");
        Console.WriteLine(
            $"Live detail: street={detail?.Street} | ward={detail?.City} | "
                + $"province={detail?.State} | country={detail?.Country}"
        );
        Check(
            detail is { Street.Length: > 0, City.Length: > 0, State.Length: > 0 },
            "live place detail fills street, ward and a listed province"
        );
        Console.WriteLine("VietMap live autocomplete/place passed");
    }

    private static VietMapGateway Gateway(
        Handler handler,
        string key,
        IntegrationDiagnosticsChecks.Writer logs
    ) =>
        new(
            new HttpClient(handler),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["VietMap:ApiKey"] = key })
                .Build(),
            new MemoryCache(new MemoryCacheOptions()),
            logs
        );

    private static async Task Unavailable(Func<Task> work, string message)
    {
        try
        {
            await work();
        }
        catch (AddressLookupUnavailableException)
        {
            return;
        }
        throw new Exception("not reported unavailable: " + message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public (HttpStatusCode Status, string Body) Reply = (HttpStatusCode.OK, "[]");
        public List<string> Requests = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(
                new HttpResponseMessage(Reply.Status)
                {
                    Content = new StringContent(Reply.Body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
