using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

return await RealtimeE2E.RunAsync(args);

internal static class RealtimeE2E
{
    private const char RecordSeparator = '\u001e';

    public static async Task<int> RunAsync(string[] args)
    {
        var options = Options.Parse(args);
        using var client = new HttpClient { BaseAddress = options.BaseUrl, Timeout = TimeSpan.FromSeconds(15) };

        try
        {
            var buyer = await LoginAsync(client, options.BuyerEmail, options.Password, "buyer");
            var seller = await LoginAsync(client, options.SellerEmail, options.Password, "seller");

            using var buyerTabOne = new SignalRProbe(client, options.BaseUrl, buyer.Token, "buyer-tab-1");
            using var buyerTabTwo = new SignalRProbe(client, options.BaseUrl, buyer.Token, "buyer-tab-2");
            using var unrelatedSeller = new SignalRProbe(client, options.BaseUrl, seller.Token, "unrelated-seller");

            await Task.WhenAll(
                buyerTabOne.ConnectAsync(),
                buyerTabTwo.ConnectAsync(),
                unrelatedSeller.ConnectAsync()
            );
            Console.WriteLine("READY: 2 buyer hubs + 1 unrelated seller hub connected through Nginx.");

            var unreadId = await FindUnreadNotificationAsync(client, buyer.Token);
            if (unreadId is null)
            {
                Console.Error.WriteLine("FAIL: buyer has no unread notification. Prepare demo data before running this test.");
                return 2;
            }

            var markRead = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/me/notifications/{unreadId.Value}/read"
            );
            markRead.Headers.Authorization = new AuthenticationHeaderValue("Bearer", buyer.Token);
            using var markReadResponse = await client.SendAsync(markRead);
            if (markReadResponse.StatusCode != HttpStatusCode.NoContent)
            {
                Console.Error.WriteLine($"FAIL: mark-read returned {(int)markReadResponse.StatusCode}.");
                return 3;
            }
            Console.WriteLine($"MUTATION: buyer marked notification {unreadId.Value} as read.");

            var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            var buyerOneEvent = buyerTabOne.WaitForTopicAsync(
                "notification-inbox",
                options.MinimumVersion,
                timeout
            );
            var buyerTwoEvent = buyerTabTwo.WaitForTopicAsync(
                "notification-inbox",
                options.MinimumVersion,
                timeout
            );
            var sellerEvent = unrelatedSeller.WaitForAnyEventAsync(options.MinimumVersion, timeout);

            var buyerEvents = await Task.WhenAll(buyerOneEvent, buyerTwoEvent);
            var sellerReceived = await sellerEvent;

            foreach (var eventInfo in buyerEvents)
                Console.WriteLine($"RECEIVED: {eventInfo.ConnectionName} version={eventInfo.Version} topics={string.Join(',', eventInfo.Topics)}");

            if (sellerReceived is not null)
            {
                Console.Error.WriteLine(
                    $"FAIL: unrelated seller received version={sellerReceived.Version} topics={string.Join(',', sellerReceived.Topics)}."
                );
                return 4;
            }

            Console.WriteLine("PASS: both buyer connections received the expected update; unrelated seller received none.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex.Message);
            return 1;
        }
    }

    private static async Task<LoginResult> LoginAsync(
        HttpClient client,
        string email,
        string password,
        string expectedRole
    )
    {
        using var response = await client.PostAsync(
            "/api/auth/login",
            JsonContent.Create(new { email, password })
        );
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var role = body.RootElement.GetProperty("role").GetString();
        var token = body.RootElement.GetProperty("token").GetString();
        if (!string.Equals(role, expectedRole, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException($"Login {email} did not return the expected {expectedRole} JWT.");
        return new LoginResult(token);
    }

    private static async Task<long?> FindUnreadNotificationAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me/notifications?page=1&pageSize=30");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var item in body.RootElement.GetProperty("items").EnumerateArray())
        {
            if (item.TryGetProperty("readAt", out var readAt) && readAt.ValueKind == JsonValueKind.Null)
                return item.GetProperty("id").GetInt64();
        }
        return null;
    }

    private sealed record LoginResult(string Token);

    private sealed class SignalRProbe : IDisposable
    {
        private readonly HttpClient _http;
        private readonly Uri _baseUrl;
        private readonly string _token;
        private readonly ClientWebSocket _socket = new();
        private readonly List<CommerceEvent> _events = [];
        private readonly object _lock = new();
        private Task? _reader;

        public SignalRProbe(HttpClient http, Uri baseUrl, string token, string name)
        {
            _http = http;
            _baseUrl = baseUrl;
            _token = token;
            Name = name;
        }

        public string Name { get; }

        public async Task ConnectAsync()
        {
            using var negotiate = new HttpRequestMessage(
                HttpMethod.Post,
                "/hubs/commerce/negotiate?negotiateVersion=1"
            );
            negotiate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            using var response = await _http.SendAsync(negotiate);
            response.EnsureSuccessStatusCode();
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var connectionToken = payload.RootElement.GetProperty("connectionToken").GetString();
            if (string.IsNullOrWhiteSpace(connectionToken))
                throw new InvalidOperationException($"{Name}: SignalR negotiate did not return connectionToken.");

            var builder = new UriBuilder(_baseUrl)
            {
                Scheme = _baseUrl.Scheme == "https" ? "wss" : "ws",
                Path = "/hubs/commerce",
                Query = $"id={Uri.EscapeDataString(connectionToken)}&access_token={Uri.EscapeDataString(_token)}",
            };
            await _socket.ConnectAsync(builder.Uri, CancellationToken.None);
            await SendAsync("{\"protocol\":\"json\",\"version\":1}" + RecordSeparator);
            var handshake = await ReceiveFrameAsync(TimeSpan.FromSeconds(10));
            if (!string.IsNullOrEmpty(handshake) && handshake != "{}")
                throw new InvalidOperationException($"{Name}: SignalR handshake rejected: {handshake}");
            _reader = Task.Run(ReadLoopAsync);
        }

        public async Task<CommerceEvent> WaitForTopicAsync(string topic, long minimumVersion, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (_lock)
                {
                    var match = _events.FirstOrDefault(x => x.Version > minimumVersion && x.Topics.Contains(topic));
                    if (match is not null)
                        return match;
                }
                await Task.Delay(100);
            }
            throw new TimeoutException($"{Name}: did not receive topic '{topic}' with version > {minimumVersion}.");
        }

        public async Task<CommerceEvent?> WaitForAnyEventAsync(long minimumVersion, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (_lock)
                {
                    var match = _events.FirstOrDefault(x => x.Version > minimumVersion);
                    if (match is not null)
                        return match;
                }
                await Task.Delay(100);
            }
            return null;
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                while (_socket.State == WebSocketState.Open)
                {
                    var frame = await ReceiveFrameAsync(Timeout.InfiniteTimeSpan);
                    if (string.IsNullOrEmpty(frame))
                        continue;
                    using var doc = JsonDocument.Parse(frame);
                    var root = doc.RootElement;
                    if (
                        !root.TryGetProperty("target", out var target)
                        || !string.Equals(target.GetString(), "CommerceChanged", StringComparison.Ordinal)
                    )
                        continue;
                    var argument = root.GetProperty("arguments")[0];
                    var version = argument.GetProperty("version").GetInt64();
                    var topics = argument
                        .GetProperty("topics")
                        .EnumerateArray()
                        .Select(x => x.GetString() ?? "")
                        .Where(x => x.Length > 0)
                        .ToHashSet(StringComparer.Ordinal);
                    lock (_lock)
                        _events.Add(new CommerceEvent(Name, version, topics));
                }
            }
            catch (WebSocketException)
            {
                // Wait methods turn an unexpected disconnect into a timeout with the connection name.
            }
        }

        private async Task SendAsync(string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task<string> ReceiveFrameAsync(TimeSpan timeout)
        {
            using var cancellation = timeout == Timeout.InfiniteTimeSpan
                ? new CancellationTokenSource()
                : new CancellationTokenSource(timeout);
            var buffer = new ArraySegment<byte>(new byte[8192]);
            var text = new StringBuilder();
            WebSocketReceiveResult result;
            do
            {
                result = await _socket.ReceiveAsync(buffer, cancellation.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException("Remote SignalR endpoint closed the connection.");
                text.Append(Encoding.UTF8.GetString(buffer.Array!, 0, result.Count));
            } while (!result.EndOfMessage);

            var records = text.ToString().Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries);
            return records.FirstOrDefault() ?? "";
        }

        public void Dispose()
        {
            try { _socket.Dispose(); } catch { }
        }
    }

    private sealed record CommerceEvent(string ConnectionName, long Version, HashSet<string> Topics);

    private sealed class Options
    {
        public Uri BaseUrl { get; private init; } = new("http://127.0.0.1:8088");
        public string BuyerEmail { get; private init; } = "buyer@demo.test";
        public string SellerEmail { get; private init; } = "seller@demo.test";
        public string Password { get; private init; } = "G4@123456";
        public long MinimumVersion { get; private init; }
        public int TimeoutSeconds { get; private init; } = 15;

        public static Options Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length; i += 2)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
                    throw new ArgumentException("Expected --name value arguments.");
                values[args[i][2..]] = args[i + 1];
            }
            return new Options
            {
                BaseUrl = new Uri(values.GetValueOrDefault("base-url", "http://127.0.0.1:8088")),
                BuyerEmail = values.GetValueOrDefault("buyer-email", "buyer@demo.test"),
                SellerEmail = values.GetValueOrDefault("seller-email", "seller@demo.test"),
                Password = values.GetValueOrDefault("password", "G4@123456"),
                MinimumVersion = long.Parse(values.GetValueOrDefault("min-version", "0")),
                TimeoutSeconds = int.Parse(values.GetValueOrDefault("timeout-seconds", "15")),
            };
        }
    }
}