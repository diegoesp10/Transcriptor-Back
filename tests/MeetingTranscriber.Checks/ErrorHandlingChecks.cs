using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MeetingTranscriber.Api.Configuration;
using MeetingTranscriber.Api.DependencyInjection;
using MeetingTranscriber.Api.Errors;
using MeetingTranscriber.Api.Middleware;
using MeetingTranscriber.Api.Security;
using MeetingTranscriber.Application;
using MeetingTranscriber.Domain;
using MeetingTranscriber.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

static class ErrorHandlingChecks
{
    public static async Task RunAsync(string root)
    {
        Check((int)FailureCode.SummaryModelNotLocal == 34, "Historical error values unchanged");
        foreach (var code in Enum.GetValues<FailureCode>())
            Check(!string.IsNullOrWhiteSpace(ErrorCatalog.Get(code).Message), "Every typed failure has a safe message");
        Check(ApiErrorClassifier.Classify(new ArgumentException("private-token-payload")).Code == FailureCode.UnexpectedError
            && ApiErrorClassifier.Classify(new JsonException("private-token-payload")).Code == FailureCode.UnexpectedError,
            "Untyped internal defects are not mislabeled as client validation");
        await CheckHttpAsync(root);
        Console.WriteLine("Error handling checks passed: real controllers, typed business failures, validation, framework errors, safe logging, engine origins, cancellation and capacity rejection without DB amplification.");
    }

    private static async Task CheckHttpAsync(string root)
    {
        var options = new TranscriptionOptions
        {
            DataRoot = Path.Combine(root, "error-checks"), WhisperModelPath = Path.Combine(root, "readiness-model.bin"),
            FfmpegPath = Path.Combine(root, "readiness-ffmpeg.exe"), MaxUploadBytes = 1000
        };
        options.Diarization.Enabled = false;
        var repository = new JsonMeetingRepository(options);
        var storage = new LocalMediaStorage(options);
        var service = new MeetingService(repository, storage);
        var queued = await service.SubmitAsync("queued.wav", "es", new MemoryStream([1]), 100, default);
        var exhausted = new Meeting { FileName = "failed.wav", Language = "es" };
        exhausted.Restore(JobStatus.Failed, 0, 0, null, options.MaxMeetingRetries);
        await repository.SaveAsync(exhausted, default);
        var completed = new Meeting { FileName = "completed.wav", Language = "es" };
        completed.Start(1); completed.Append([new(0, 1, "Texto", "Hablante 1")]); completed.Complete();
        await repository.SaveAsync(completed, default);
        var large = new Meeting { FileName = "large.wav", Language = "es" };
        large.Start(1); large.Append([new(0, 1, new string('a', 500_001), null)]); large.Complete();
        await repository.SaveAsync(large, default);
        var errors = new TestErrorLog();
        var summary = new TestSummaryProvider();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RequestLimits:ReadPerMinute"] = "100", ["RequestLimits:UploadPerMinute"] = "100",
            ["RequestLimits:WritePerMinute"] = "100", ["RequestLimits:RetryPerMinute"] = "100",
            ["RequestLimits:SummaryPerMinute"] = "100", ["RequestLimits:PerIpBurstRequests"] = "100",
            ["RequestLimits:MaxTrackedIps"] = "2"
        });
        builder.Services.AddApiLayer(builder.Configuration);
        builder.Services.AddControllers().AddApplicationPart(typeof(MeetingTranscriber.Api.Controllers.MeetingsController).Assembly);
        builder.Services.AddApplicationLayer();
        builder.Services.Configure<SecurityOptions>(settings => settings.ApiKey = "isolated-error-check-abcdefghijklmnopqrstuvwxyz");
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IMeetingRepository>(repository);
        builder.Services.AddSingleton<IMediaStorage>(storage);
        builder.Services.AddSingleton<IErrorLog>(errors);
        builder.Services.AddSingleton<IMeetingSummaryProvider>(summary);
        builder.Services.AddSingleton<IAudioPreprocessor>(new FakePreprocessor());
        builder.Services.AddSingleton<ITranscriptionProvider>(new FakeProvider());
        builder.Services.AddSingleton<ISpeakerDiarizer>(new FakeDiarizer());
        await using var app = builder.Build();
        // Socket substitution only exists inside this isolated test host.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/test/capacity") context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.90");
            await next(context);
        });
        app.UseApiPipeline();
        app.MapControllers();
        app.MapGet("/test/unknown", IResult () => throw new InvalidOperationException("private-token-payload"));
        app.MapGet("/test/whisper", IResult () => throw new TranscriptionException(FailureCode.WhisperInferenceFailed));
        app.MapGet("/test/timeout", IResult () => throw new TranscriptionException(FailureCode.LocalInferenceTimeout, source: ErrorSource.Summary));
        app.MapGet("/test/cancel", (HttpContext context) =>
        {
            context.RequestAborted = new CancellationToken(canceled: true);
            throw new OperationCanceledException(context.RequestAborted);
        });
        app.MapGet("/test/capacity", () => Results.Ok());
        await app.StartAsync();
        try
        {
            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new(url) };
            client.DefaultRequestHeaders.Add("X-Api-Key", "isolated-error-check-abcdefghijklmnopqrstuvwxyz");
            async Task AssertError(string path, HttpStatusCode status, FailureCode code, HttpMethod? method = null, HttpContent? content = null)
            {
                using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path) { Content = content };
                using var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                Check(response.StatusCode == status, $"{path}: expected {status}, got {response.StatusCode}: {body}");
                Check(!string.IsNullOrWhiteSpace(body), $"{path}: empty body for {response.StatusCode}, content type {response.Content.Headers.ContentType}");
                using var json = JsonDocument.Parse(body);
                var error = ErrorCatalog.Get(code);
                var legacy = ApiErrorPolicy.UsesLegacyContract(error);
                Check(json.RootElement.GetProperty(legacy ? "error" : "code").GetString() == code.ToString(), "Stable error code");
                Check(response.Headers.Contains("X-Correlation-ID"), "Correlation header on every controlled failure");
                if (!legacy) Check(json.RootElement.GetProperty("correlationId").GetString() == response.Headers.GetValues("X-Correlation-ID").Single(), "Problem correlation matches header");
                Check(!body.Contains("private-token-payload") && !body.Contains(root, StringComparison.OrdinalIgnoreCase), "No private payloads or paths");
            }
            var missing = Guid.NewGuid();
            foreach (var suffix in new[] { "", "/transcript", "/transcript.txt", "/recording", "/local-export" })
                await AssertError($"/api/meetings/{missing}{suffix}", HttpStatusCode.NotFound, FailureCode.MeetingNotFound);
            await AssertError($"/api/meetings/{missing}/summary", HttpStatusCode.NotFound, FailureCode.MeetingNotFound, HttpMethod.Post);
            foreach (var suffix in new[] { "/transcript", "/transcript.txt" })
                await AssertError($"/api/meetings/{queued.Id}{suffix}", HttpStatusCode.Conflict, FailureCode.TranscriptNotCompleted);
            await AssertError($"/api/meetings/{queued.Id}/summary", HttpStatusCode.Conflict, FailureCode.TranscriptNotCompleted, HttpMethod.Post);
            await AssertError($"/api/meetings/{queued.Id}/retry", HttpStatusCode.Conflict, FailureCode.MeetingNotFailedOrRetryLimitExceeded, HttpMethod.Post);
            await AssertError($"/api/meetings/{exhausted.Id}/retry", HttpStatusCode.Conflict, FailureCode.MeetingRetryLimitExceeded, HttpMethod.Post);
            await AssertError($"/api/meetings/{queued.Id}/local-export/confirm", HttpStatusCode.BadRequest, FailureCode.LocalFilesNotSaved, HttpMethod.Post, JsonContent.Create(new { filesSaved = false }));
            await AssertError($"/api/meetings/{queued.Id}/local-export/confirm", HttpStatusCode.Conflict, FailureCode.MeetingStillActive, HttpMethod.Post, JsonContent.Create(new { filesSaved = true }));
            await AssertError($"/api/meetings/{completed.Id}/recording", HttpStatusCode.NotFound, FailureCode.RecordingNotFound);
            await AssertError($"/api/meetings/{queued.Id}/local-export/confirm", HttpStatusCode.BadRequest, FailureCode.RequestValidationFailed, HttpMethod.Post, new StringContent("{invalid-json", Encoding.UTF8, "application/json"));
            await AssertError("/api/meetings?fileName=a.wav", HttpStatusCode.UnsupportedMediaType, FailureCode.UnsupportedMediaType, HttpMethod.Post, new StringContent("private-token-payload"));
            await AssertError("/api/meetings", HttpStatusCode.BadRequest, FailureCode.RequestValidationFailed, HttpMethod.Post, Upload([1]));
            await AssertError("/api/meetings?fileName=a.wav", HttpStatusCode.ServiceUnavailable, FailureCode.WhisperModelMissing, HttpMethod.Post, Upload([1]));
            File.WriteAllBytes(options.WhisperModelPath, [1]); File.WriteAllBytes(options.FfmpegPath, [1]);
            await AssertError("/api/meetings?fileName=a.exe", HttpStatusCode.BadRequest, FailureCode.UnsupportedMediaFormat, HttpMethod.Post, Upload([1]));
            await AssertError("/api/meetings?fileName=a.wav&language=spanish", HttpStatusCode.BadRequest, FailureCode.InvalidLanguage, HttpMethod.Post, Upload([1]));
            await AssertError("/api/meetings?fileName=a.wav", HttpStatusCode.BadRequest, FailureCode.EmptyUpload, HttpMethod.Post, Upload([]));
            await AssertError("/api/meetings?fileName=a.wav", HttpStatusCode.RequestEntityTooLarge, FailureCode.UploadTooLarge, HttpMethod.Post, Upload(new byte[1001]));
            using (var upload = await client.PostAsync("/api/meetings?fileName=valid.wav", Upload([1])))
                Check(upload.StatusCode == HttpStatusCode.Accepted && upload.Headers.Location is not null, "Successful submission and polling location preserved");
            foreach (var suffix in new[] { "", "/transcript", "/transcript.txt", "/local-export" })
            {
                using var success = await client.GetAsync($"/api/meetings/{completed.Id}{suffix}");
                Check(success.StatusCode == HttpStatusCode.OK, "Successful meeting reads preserved");
            }
            using (var success = await client.PostAsync($"/api/meetings/{completed.Id}/summary", null))
                Check(success.StatusCode == HttpStatusCode.OK, "Successful summary preserved");
            await AssertError($"/api/meetings/{large.Id}/summary", HttpStatusCode.RequestEntityTooLarge, FailureCode.SummaryInputTooLarge, HttpMethod.Post);
            summary.Failure = new SummaryProviderException();
            await AssertError($"/api/meetings/{completed.Id}/summary", HttpStatusCode.BadGateway, FailureCode.SummaryInvalidResponse, HttpMethod.Post);
            await AssertError("/test/whisper", HttpStatusCode.InternalServerError, FailureCode.WhisperInferenceFailed);
            await AssertError("/test/timeout", HttpStatusCode.GatewayTimeout, FailureCode.LocalInferenceTimeout);
            await AssertError("/test/unknown", HttpStatusCode.InternalServerError, FailureCode.UnexpectedError);
            await AssertError("/route-does-not-exist", HttpStatusCode.NotFound, FailureCode.ResourceNotFound);
            await AssertError($"/api/meetings/{queued.Id}", HttpStatusCode.MethodNotAllowed, FailureCode.HttpMethodNotAllowed, HttpMethod.Delete);
            using (var cancelled = await client.GetAsync("/test/cancel")) { }
            Check(errors.Entries.Count == 5, "Only five operational failures were recorded; no business/security/cancellation rows");
            Check(errors.Entries.Single(e => e.Code == FailureCode.WhisperInferenceFailed).Source == ErrorSource.Transcription, "Whisper source is transcription");
            Check(errors.Entries.Single(e => e.Code == FailureCode.LocalInferenceTimeout).Source == ErrorSource.Summary, "Summary timeout keeps its origin");
            Check(errors.Entries.Single(e => e.Code == FailureCode.SummaryInvalidResponse).Source == ErrorSource.Summary, "Summary source and typed code");
            Check(!JsonSerializer.Serialize(errors.Entries).Contains("private-token-payload"), "Stored traces sanitized");
            using (var confirmed = await client.PostAsJsonAsync($"/api/meetings/{completed.Id}/local-export/confirm", new { filesSaved = true }))
                Check(confirmed.StatusCode == HttpStatusCode.NoContent && await repository.GetAsync(completed.Id, default) is null,
                    "Successful final export cleanup preserved");
            var guard = app.Services.GetRequiredService<RequestAbuseGuard>();
            Check(guard.TryEnter("192.0.2.80", false, out var admission) is null, "Fill last security slot"); admission!.Dispose();
            for (var i = 0; i < 20; i++)
                await AssertError("/test/capacity", HttpStatusCode.ServiceUnavailable, FailureCode.SecurityCapacityReached);
            Check(errors.Entries.Count == 5, "503 capacity flood does not write DB rows");
            errors.FailWrites = true;
            await AssertError("/test/unknown", HttpStatusCode.InternalServerError, FailureCode.UnexpectedError);
        }
        finally { await app.StopAsync(); }
    }

    private static HttpContent Upload(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class TestSummaryProvider : IMeetingSummaryProvider
    {
        public Exception? Failure { get; set; }
        public Task<MeetingSummary> GenerateAsync(IReadOnlyList<TranscriptSegment> segments, string language, CancellationToken ct)
            => Failure is { } failure ? throw failure : Task.FromResult(new MeetingSummary("Resumen", [], [], [], []));
    }
    private sealed class TestErrorLog : IErrorLog
    {
        public List<ErrorLogEntry> Entries { get; } = [];
        public bool FailWrites { get; set; }
        public Task RecordAsync(ErrorLogEntry entry, CancellationToken ct = default)
        {
            if (FailWrites) throw new InvalidOperationException("private-token-payload");
            Entries.Add(entry); return Task.CompletedTask;
        }
        public Task<IReadOnlyList<ErrorLogEntry>> ListAsync(Guid? meetingId, string? correlationId, int take, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ErrorLogEntry>>(Entries);
    }
}
