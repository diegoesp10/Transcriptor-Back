using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using MeetingTranscriber.Api.DependencyInjection;
using MeetingTranscriber.Api.Middleware;
using MeetingTranscriber.Application;
using MeetingTranscriber.Infrastructure;
using MeetingTranscriber.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

static class ReliabilityChecks
{
    public static async Task ExportOpenApiAsync(string path)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddApiLayer(builder.Configuration);
        builder.Services.AddControllers().AddApplicationPart(typeof(MeetingTranscriber.Api.Controllers.MeetingsController).Assembly);
        builder.Services.AddApplicationLayer();
        builder.Services.AddSingleton(new TranscriptionOptions());
        builder.Services.AddSingleton<IErrorLog>(new RecordingErrorLog());
        // Metadata only: neither the database nor the provider is instantiated or called.
        builder.Services.AddSingleton<IMeetingRepository>(_ => throw new InvalidOperationException("Metadata-only host"));
        builder.Services.AddSingleton<IMediaStorage>(_ => throw new InvalidOperationException("Metadata-only host"));
        await using var app = builder.Build();
        app.MapControllers();
        app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous().ExcludeFromDescription();
        await app.StartAsync();
        try
        {
            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient();
            var json = await client.GetStringAsync(url + "/openapi/v1.json");
            await File.WriteAllTextAsync(path, json);
        }
        finally { await app.StopAsync(); }
    }

    public static async Task RunAsync()
    {
        await new EfErrorLog(new UnavailableDbFactory(), NullLogger<EfErrorLog>.Instance).RecordAsync(
            new ErrorLogEntry { Source = ErrorSource.Worker, Code = FailureCode.WorkerPersistenceFailed, CorrelationId = "test" });
        await CheckHttpAsync();
        Console.WriteLine("Reliability checks passed: durable retry limit, safe errors, HTTP rate limits and correlation.");
    }

    private static void Check(bool valid, string name) { if (!valid) throw new Exception(name); }

    private static async Task CheckHttpAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RequestLimits:RetryPerMinute"] = "1", ["RequestLimits:SummaryPerMinute"] = "1",
            ["RequestLimits:UploadPerMinute"] = "1", ["RequestLimits:ReadPerMinute"] = "2"
        });
        var recorder = new RecordingErrorLog();
        builder.Services.AddSingleton<IErrorLog>(recorder);
        builder.Services.AddProblemDetails();
        builder.Services.AddApiErrorHandling();
        builder.Services.AddApiRateLimits(builder.Configuration);
        await using var app = builder.Build();
        app.UseMiddleware<RequestExceptionMiddleware>();
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.ContainsKey("X-Test-Authenticated"))
                context.User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test-client")], "test"));
            await next(context);
        });
        app.UseRateLimiter();
        var retryCalls = 0;
        app.MapPost("/api/meetings/{id:guid}/retry", () => { retryCalls++; return Results.Accepted(); });
        app.MapPost("/api/meetings", () => Results.Accepted());
        app.MapPost("/api/meetings/{id:guid}/summary", () => Results.Ok());
        app.MapGet("/api/meetings/{id:guid}", () => Results.Ok());
        app.MapGet("/test/failure", Failure);
        app.MapGet("/test/quota", Quota);
        await app.StartAsync();
        try
        {
            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(url) };
            client.DefaultRequestHeaders.Add("X-Test-Authenticated", "yes");
            using var first = await client.PostAsync($"/api/meetings/{Guid.NewGuid()}/retry", null);
            using var second = await client.PostAsync($"/api/meetings/{Guid.NewGuid()}/retry?random=1", null);
            Check(first.StatusCode == HttpStatusCode.Accepted && second.StatusCode == HttpStatusCode.TooManyRequests
                && retryCalls == 1 && second.Headers.RetryAfter?.Delta > TimeSpan.Zero, "Route IDs cannot bypass retry rate limit");
            Check(second.Headers.Contains("X-Correlation-ID"), "Rejected request correlation");
            using var upload = await client.PostAsync("/api/meetings", null);
            using var uploadBlocked = await client.PostAsync("/api/meetings", null);
            using var summary = await client.PostAsync($"/api/meetings/{Guid.NewGuid()}/summary", null);
            using var summaryBlocked = await client.PostAsync($"/api/meetings/{Guid.NewGuid()}/summary", null);
            Check(upload.StatusCode == HttpStatusCode.Accepted && uploadBlocked.StatusCode == HttpStatusCode.TooManyRequests
                && summary.StatusCode == HttpStatusCode.OK && summaryBlocked.StatusCode == HttpStatusCode.TooManyRequests,
                "Upload and summary limits independent");
            using var read = await client.GetAsync($"/api/meetings/{Guid.NewGuid()}");
            Check(read.StatusCode == HttpStatusCode.OK, "Polling remains available");
            using var failure = await client.GetAsync("/test/failure");
            var failureBody = await failure.Content.ReadAsStringAsync();
            Check(failure.StatusCode == HttpStatusCode.InternalServerError && !failureBody.Contains("secret-request-payload")
                && failureBody.Contains("correlationId"), "Safe API problem response");
            using var quota = await client.GetAsync("/test/quota");
            Check(quota.StatusCode == HttpStatusCode.ServiceUnavailable
                && (await quota.Content.ReadAsStringAsync()).Contains("ProviderQuotaExceeded"), "API quota problem code");
            Check(recorder.Entries.Count == 2 && !JsonSerializer.Serialize(recorder.Entries).Contains("secret-request-payload"),
                "Failure persisted; rejected floods do not write rows");
            client.DefaultRequestHeaders.Remove("X-Test-Authenticated");
            using var anonymous = await client.PostAsync($"/api/meetings/{Guid.NewGuid()}/retry", null);
            using var anonymousBlocked = await client.PostAsync($"/api/meetings/{Guid.NewGuid()}/retry", null);
            Check(anonymousBlocked.StatusCode == HttpStatusCode.TooManyRequests, "Anonymous callers also rate limited");
        }
        finally { await app.StopAsync(); }
    }

    private sealed class SequenceHandler(Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(++Count));
    }
    private sealed class UnavailableDbFactory : IDbContextFactory<MeetingTranscriberDbContext>
    { public MeetingTranscriberDbContext CreateDbContext() => throw new InvalidOperationException("DB unavailable"); }

    private static IResult Failure() => throw new InvalidOperationException("secret-request-payload");
    private static IResult Quota() => throw new TranscriptionException(FailureCode.ProviderQuotaExceeded, 429, "credit_balance_exhausted", "req_test", 1);
}

sealed class RecordingErrorLog : IErrorLog
{
    public List<ErrorLogEntry> Entries { get; } = [];
    public Task RecordAsync(ErrorLogEntry entry, CancellationToken ct = default) { Entries.Add(entry); return Task.CompletedTask; }
    public Task<IReadOnlyList<ErrorLogEntry>> ListAsync(Guid? meetingId, string? correlationId, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ErrorLogEntry>>(Entries.Take(take).ToArray());
}

