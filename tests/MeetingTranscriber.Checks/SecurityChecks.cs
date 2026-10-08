using System.Net;
using System.Text.Json;
using MeetingTranscriber.Api.Configuration;
using MeetingTranscriber.Api.DependencyInjection;
using MeetingTranscriber.Api.Middleware;
using MeetingTranscriber.Api.Security;
using MeetingTranscriber.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

static class SecurityChecks
{
    private const string TestKey = "isolated-test-key-abcdefghijklmnopqrstuvwxyz";

    public static async Task RunAsync()
    {
        CheckBoundedState();
        CheckProductionKey();
        await CheckHttpAsync();
        Console.WriteLine("Security checks passed: per-IP budgets, brute-force bans, expiry, spoofed headers, burst/preflight control, bounded memory, upload concurrency and production credentials.");
    }

    private static void CheckBoundedState()
    {
        var clock = new TestClock();
        var guard = new RequestAbuseGuard(new() { MaxTrackedIps = 2, AuthenticationFailuresPerMinute = 2 }, clock);
        Check(guard.TryEnter("one", false, out var first) is null, "First IP admitted"); first!.Dispose();
        Check(guard.TryEnter("two", false, out var second) is null, "Second IP admitted"); second!.Dispose();
        Check(guard.TryEnter("three", false, out _)?.Code == "SecurityCapacityReached" && guard.TrackedIps == 2, "IP flood cannot allocate unlimited state");
        guard.Record("one", AbuseSignal.Authentication); guard.Record("one", AbuseSignal.Authentication);
        Check(guard.TryEnter("one", false, out _)?.RetryAfterSeconds == 120, "Initial ban duration");
        clock.Advance(TimeSpan.FromSeconds(10));
        guard.Record("one", AbuseSignal.Authentication);
        Check(guard.TryEnter("one", false, out _)?.RetryAfterSeconds == 110, "Rejected traffic cannot extend a ban");
        clock.Advance(TimeSpan.FromSeconds(110));
        Check(guard.TryEnter("one", false, out var afterExpiry) is null, "Ban expires without sleep"); afterExpiry!.Dispose();
        guard.Record("one", AbuseSignal.Authentication); guard.Record("one", AbuseSignal.Authentication);
        Check(guard.TryEnter("one", false, out _)?.RetryAfterSeconds == 240, "Repeated abuse escalates penalty");
        clock.Advance(TimeSpan.FromMinutes(31));
        Check(guard.TryEnter("three", false, out var recovered) is null && guard.TrackedIps == 1, "Idle state is reclaimed"); recovered!.Dispose();
        Check(ClientAddress.Normalize(IPAddress.Parse("::ffff:192.0.2.1")) == ClientAddress.Normalize(IPAddress.Parse("192.0.2.1")), "IPv4 mapped aliases share one identity");
        var concurrency = new RequestAbuseGuard(new() { PerIpConcurrentRequests = 2 }, clock);
        concurrency.TryEnter("same", false, out var activeOne);
        concurrency.TryEnter("same", false, out var activeTwo);
        Check(concurrency.TryEnter("same", false, out _)?.Code == "RequestConcurrencyLimited", "Per-IP concurrent request ceiling");
        activeOne!.Dispose(); activeOne.Dispose();
        Check(concurrency.TryEnter("same", false, out var released) is null, "Completion releases exactly one slot");
        released!.Dispose(); activeTwo!.Dispose();
        var global = new RequestAbuseGuard(new() { GlobalPerMinute = 2 }, clock);
        global.TryEnter("one", false, out var one); one!.Dispose();
        global.TryEnter("two", false, out var two); two!.Dispose();
        Check(global.TryEnter("three", false, out _)?.Code == "GlobalRequestLimited", "Global budget spans different IPs");
        clock.Advance(TimeSpan.FromMinutes(1));
        Check(global.TryEnter("three", false, out var globalRecovered) is null, "Global rolling window recovers"); globalRecovered!.Dispose();
    }

    private static void CheckProductionKey()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Security:ApiKey"] = "meeting-transcriber-local-development-key" });
        builder.Services.AddApiConfiguration(builder.Configuration, builder.Environment);
        using var provider = builder.Services.BuildServiceProvider();
        try { _ = provider.GetRequiredService<IOptions<SecurityOptions>>().Value; throw new Exception("Development credential accepted in production"); }
        catch (OptionsValidationException) { }
    }

    private static async Task CheckHttpAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RequestLimits:ReadPerMinute"] = "5", ["RequestLimits:PerIpPerMinute"] = "12",
            ["RequestLimits:PerIpBurstRequests"] = "8", ["RequestLimits:UnknownRoutesPerMinute"] = "3",
            ["RequestLimits:EndpointPerMinute:GET /api/custom/{id}"] = "1",
            ["Cors:AllowedOrigins:0"] = "http://localhost:5173"
        });
        var clock = new TestClock();
        var errors = new RecordingErrorLog();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton<IErrorLog>(errors);
        // This host exercises middleware with minimal endpoints, not meeting use cases.
        builder.Services.AddSingleton(new MeetingTranscriber.Infrastructure.TranscriptionOptions());
        builder.Services.AddSingleton<MeetingService>(_ => throw new InvalidOperationException("Middleware-only test host"));
        builder.Services.Configure<SecurityOptions>(options => options.ApiKey = TestKey);
        builder.Services.AddApiLayer(builder.Configuration);
        await using var app = builder.Build();
        // Test-only socket substitution exercises many real clients on one loopback Kestrel host.
        app.Use(async (context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers["X-Test-Socket"].ToString());
            await next(context);
        });
        app.UseApiPipeline();
        app.MapGet("/health", () => Results.Ok()).AllowAnonymous();
        app.MapGet("/api/meetings/{id:guid}", () => Results.Ok());
        app.MapGet("/api/business-missing/{id:guid}", () => Results.NotFound());
        app.MapGet("/api/custom/{id:guid}", () => Results.Ok());
        foreach (var route in new[] { "a", "b", "c" }) app.MapGet("/api/read-" + route, () => Results.Ok());
        app.MapPost("/api/small-body", async (HttpContext context) =>
        {
            await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted);
            return Results.Ok();
        });
        var uploadsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishUploads = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uploadCalls = 0;
        app.MapPost("/api/meetings", async () =>
        {
            if (Interlocked.Increment(ref uploadCalls) == 2) uploadsStarted.TrySetResult();
            await finishUploads.Task;
            return Results.Accepted();
        });
        await app.StartAsync();
        try
        {
            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new(url), Timeout = TimeSpan.FromSeconds(20) };
            async Task<HttpResponseMessage> Send(string ip, string path, string? key = TestKey, HttpMethod? method = null, HttpContent? content = null, bool duplicate = false)
            {
                using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path) { Content = content };
                request.Headers.Add("X-Test-Socket", ip);
                request.Headers.Add("X-Forwarded-For", "198.51.100." + Random.Shared.Next(1, 200));
                request.Headers.Add("X-Real-IP", "203.0.113." + Random.Shared.Next(1, 200));
                request.Headers.Add("Origin", "http://localhost:5173");
                if (key is not null) request.Headers.Add("X-Api-Key", duplicate ? [key, key] : [key]);
                if (method == HttpMethod.Options)
                {
                    request.Headers.Add("Access-Control-Request-Method", "GET");
                }
                return await client.SendAsync(request);
            }
            async Task AssertStatus(string ip, string path, HttpStatusCode expected, string? key = TestKey, HttpMethod? method = null, HttpContent? content = null)
            {
                using var response = await Send(ip, path, key, method, content);
                Check(response.StatusCode == expected, $"HTTP {ip} {path}: expected {expected}, received {response.StatusCode}");
                Check(response.Headers.Contains("X-Correlation-ID") && response.Headers.Contains("X-Content-Type-Options"), "Security and correlation headers");
                Check(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins)
                    && origins.Single() == "http://localhost:5173", "Permitted browser can read early security rejections");
                if (path.StartsWith("/api/")) Check(response.Headers.CacheControl?.NoStore == true, "Private API responses not cached");
            }
            for (var attempt = 0; attempt < 5; attempt++) await AssertStatus("192.0.2.10", "/api/read-a", HttpStatusCode.Unauthorized, "wrong-key-" + attempt);
            using (var ban = await Send("192.0.2.10", "/api/read-a"))
            {
                Check(ban.StatusCode == HttpStatusCode.Forbidden && ban.Headers.RetryAfter?.Delta == TimeSpan.FromSeconds(120)
                    && (await ban.Content.ReadAsStringAsync()).Contains("ClientTemporarilyBlocked"), "Brute force blocked despite changing spoofed IP headers");
            }
            await AssertStatus("192.0.2.11", "/api/read-a", HttpStatusCode.OK);
            using (var duplicate = await Send("192.0.2.12", "/api/read-a", duplicate: true)) Check(duplicate.StatusCode == HttpStatusCode.Unauthorized, "Duplicate API key header rejected");
            await AssertStatus("192.0.2.13", "/api/read-a", HttpStatusCode.Unauthorized, new string('x', 257));
            for (var request = 0; request < 5; request++) await AssertStatus("192.0.2.20", $"/api/meetings/{Guid.NewGuid()}?random={request}", HttpStatusCode.OK);
            await AssertStatus("::ffff:192.0.2.20", $"/api/meetings/{Guid.NewGuid()}", HttpStatusCode.TooManyRequests);
            await AssertStatus("192.0.2.21", $"/api/meetings/{Guid.NewGuid()}", HttpStatusCode.OK);
            for (var request = 0; request < 2; request++) await AssertStatus("192.0.2.20", $"/api/meetings/{Guid.NewGuid()}", HttpStatusCode.TooManyRequests);
            await AssertStatus("192.0.2.20", "/health", HttpStatusCode.Forbidden, null);
            clock.Advance(TimeSpan.FromSeconds(120));
            await AssertStatus("192.0.2.20", "/health", HttpStatusCode.OK, null);
            for (var request = 0; request < 3; request++) await AssertStatus("192.0.2.30", "/unknown-" + request, HttpStatusCode.NotFound);
            await AssertStatus("192.0.2.30", "/health", HttpStatusCode.Forbidden, null);
            for (var request = 0; request < 4; request++) await AssertStatus("192.0.2.31", "/api/business-missing/" + Guid.NewGuid(), HttpStatusCode.NotFound);
            await AssertStatus("192.0.2.31", "/health", HttpStatusCode.OK, null);
            for (var request = 0; request < 12; request++)
            {
                if (request == 6) clock.Advance(TimeSpan.FromSeconds(11));
                await AssertStatus("192.0.2.40", "/api/read-" + (request < 5 ? "a" : request < 10 ? "b" : "c"), HttpStatusCode.OK);
            }
            clock.Advance(TimeSpan.FromSeconds(11));
            await AssertStatus("192.0.2.40", "/health", HttpStatusCode.TooManyRequests, null);
            await AssertStatus("192.0.2.41", "/health", HttpStatusCode.OK, null);
            await AssertStatus("192.0.2.45", "/api/custom/" + Guid.NewGuid(), HttpStatusCode.OK);
            await AssertStatus("192.0.2.45", "/api/custom/" + Guid.NewGuid(), HttpStatusCode.TooManyRequests);
            await AssertStatus("192.0.2.45", "/api/read-a", HttpStatusCode.OK);
            for (var request = 0; request < 8; request++) await AssertStatus("192.0.2.50", "/api/read-a", HttpStatusCode.NoContent, null, HttpMethod.Options);
            await AssertStatus("192.0.2.50", "/api/read-a", HttpStatusCode.TooManyRequests, null, HttpMethod.Options);
            await AssertStatus("192.0.2.50", "/api/read-a", HttpStatusCode.TooManyRequests, null, HttpMethod.Options);
            await AssertStatus("192.0.2.50", "/api/read-a", HttpStatusCode.Forbidden, null, HttpMethod.Options);
            await AssertStatus("192.0.2.60", "/api/small-body", HttpStatusCode.RequestEntityTooLarge, method: HttpMethod.Post, content: new ByteArrayContent(new byte[17000]));
            await AssertStatus("192.0.2.62", "/api/small-body", HttpStatusCode.RequestEntityTooLarge, method: HttpMethod.Post, content: new ChunkedBody());
            await AssertStatus("192.0.2.61", "/api/read-a", HttpStatusCode.MethodNotAllowed, method: new("TRACE"));
            var firstUpload = Send("192.0.2.70", "/api/meetings", method: HttpMethod.Post);
            var secondUpload = Send("192.0.2.71", "/api/meetings", method: HttpMethod.Post);
            await uploadsStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                await AssertStatus("192.0.2.72", "/api/meetings", HttpStatusCode.TooManyRequests, method: HttpMethod.Post);
                await AssertStatus("192.0.2.70", "/api/meetings", HttpStatusCode.TooManyRequests, method: HttpMethod.Post);
                await AssertStatus("192.0.2.70", "/health", HttpStatusCode.OK, null);
            }
            finally { finishUploads.TrySetResult(); }
            using var firstResult = await firstUpload;
            using var secondResult = await secondUpload;
            Check(firstResult.StatusCode == HttpStatusCode.Accepted && secondResult.StatusCode == HttpStatusCode.Accepted && uploadCalls == 2, "Heavy concurrency rejected before endpoint execution");
            await AssertStatus("192.0.2.72", "/api/meetings", HttpStatusCode.Accepted, method: HttpMethod.Post);
            Check(errors.Entries.Count == 0, "Attack rejection does not amplify database writes");
        }
        finally { finishUploads.TrySetResult(); await app.StopAsync(); }
    }

    private static void Check(bool result, string name) { if (!result) throw new Exception(name); }
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
    private sealed class ChunkedBody : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(new byte[17000]).AsTask();
    }
}
