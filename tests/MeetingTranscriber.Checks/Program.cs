using MeetingTranscriber.Application;
using MeetingTranscriber.Domain;
using MeetingTranscriber.Infrastructure;
using MeetingTranscriber.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using System.Text.Json;

var root = Path.Combine(Path.GetTempPath(), "meeting-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var options = new TranscriptionOptions { DataRoot = root };
    var repository = new JsonMeetingRepository(options);
    var storage = new LocalMediaStorage(options);
    var service = new MeetingService(repository, storage);
    var errorLog = new RecordingErrorLog();
    var meeting = await service.SubmitAsync("../meeting.mp4", "es", new MemoryStream([1, 2, 3]), 100, default);
    Check(meeting.FileName == "meeting.mp4", "Safe filename");
    Check((await new JsonMeetingRepository(options).GetAsync(meeting.Id, default))?.Status == JobStatus.Queued, "Durable queue");
    var processor = new ProcessMeeting(repository, storage, new FakePreprocessor(), new FakeProvider(), errorLog, new FakeDiarizer());
    await processor.ExecuteAsync(meeting.Id, default);
    var completed = (await repository.GetAsync(meeting.Id, default))!;
    Check(completed.Status == JobStatus.Completed && completed.CompletedChunks == 2, "Completion");
    Check(completed.Segments[1].StartSeconds == 601 && completed.Segments[1].Speaker == "Hablante 1", "Global timing / meeting speaker scope");
    await Expect<InvalidOperationException>(() => { completed.Start(1); return Task.CompletedTask; });
    await Expect<UploadTooLargeException>(() => storage.StoreAsync(Guid.NewGuid(), new MemoryStream([1, 2, 3]), 2, default));
    await Expect<ErrorException>(() => service.SubmitAsync("meeting.exe", "es", new MemoryStream([1]), 100, default));
    var failed = await service.SubmitAsync("meeting.wav", "es", new MemoryStream([1]), 100, default);
    await new ProcessMeeting(repository, storage, new FakePreprocessor(), new FailingProvider(), errorLog, new FakeDiarizer()).ExecuteAsync(failed.Id, default);
    failed = (await repository.GetAsync(failed.Id, default))!;
    Check(failed.Status == JobStatus.Failed && failed.ErrorCode == "ProcessingFailed", "Sanitized failure");
    Check(errorLog.Entries.Single().MeetingId == failed.Id && errorLog.Entries.Single().ChunkIndex == 0
        && !JsonSerializer.Serialize(errorLog.Entries).Contains("Confidential vendor payload"), "Controlled processing error trace");
    var retries = await Task.WhenAll(repository.TryRetryAsync(failed.Id, default), repository.TryRetryAsync(failed.Id, default));
    Check(retries.Count(r => r) == 1, "Atomic retry");
    failed = (await repository.GetAsync(failed.Id, default))!;
    Check(failed.Status == JobStatus.Queued && failed.Segments.Count == 0, "Explicit retry");
    for (var retry = 1; retry < 3; retry++)
    {
        failed.Fail("Test"); await repository.SaveAsync(failed, default);
        Check(await repository.TryRetryAsync(failed.Id, default), "Retry within limit");
        failed = (await repository.GetAsync(failed.Id, default))!;
    }
    failed.Fail("Test"); await repository.SaveAsync(failed, default);
    Check(!await repository.TryRetryAsync(failed.Id, default) && failed.RetryCount == 3, "Durable meeting retry limit");
    var wav = Path.Combine(root, "sample.wav");
    using (var writer = new BinaryWriter(File.Create(wav)))
    {
        writer.Write("RIFF"u8); writer.Write(36 + 32000); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000);
        writer.Write(32000); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8);
        writer.Write(32000); writer.Write(new byte[32000]);
    }
    Check(FfmpegPreprocessor.WavDuration(wav) == 1, "PCM timing");
    options.FfmpegPath = Path.GetFullPath("tools/ffmpeg/ffmpeg.exe");
    if (File.Exists(options.FfmpegPath))
    {
        await using var sample = File.OpenRead(wav);
        var realMedia = await service.SubmitAsync("sample.wav", "es", sample, 100000, default);
        var chunks = await new FfmpegPreprocessor(storage, options).PrepareAsync(realMedia.Id, default);
        Check(chunks.Count == 1 && Math.Abs(chunks[0].DurationSeconds - 1) < .01, "Real FFmpeg conversion");
    }
    options.FfmpegPath = Path.Combine(root, "missing-ffmpeg.exe");
    var missingTool = await service.SubmitAsync("missing.wav", "es", new MemoryStream([1]), 100, default);
    await new ProcessMeeting(repository, storage, new FfmpegPreprocessor(storage, options), new FakeProvider(), errorLog, new FakeDiarizer()).ExecuteAsync(missingTool.Id, default);
    Check((await repository.GetAsync(missingTool.Id, default))!.ErrorCode == "FFmpegUnavailable", "Missing FFmpeg diagnosed");
    options.WhisperModelPath = Path.Combine(root, "missing.bin");
    Check(TranscriptionReadiness.ErrorCode(options) == "WhisperModelMissing", "Missing local model diagnosed");
    var transient = await service.SubmitAsync("temporary.wav", "es", new MemoryStream([1]), 100, default, 24);
    Check(transient.TemporaryExpiresAt > DateTimeOffset.UtcNow, "Temporary expiry persisted");
    Check(!await repository.DeleteFinalizedAsync(transient.Id, default), "Active jobs cannot be deleted");
    transient.Fail("Test"); await repository.SaveAsync(transient, default);
    Check(await new TemporaryMeetingCleanup(repository, storage).DeleteAsync(transient.Id, default), "Finalized temporary cleanup");
    Check(await repository.GetAsync(transient.Id, default) is null && !File.Exists(storage.SourcePath(transient.Id)), "Media and transcript deleted");
    await LocalEngineChecks.RunAsync(root, wav, args.Contains("--whisper"));
    var speechArgument = Array.IndexOf(args, "--speech");
    if (speechArgument >= 0) await LocalEngineChecks.CheckSpeechAsync(root, args[speechArgument + 1]);
    var speakerArgument = Array.IndexOf(args, "--speakers");
    if (speakerArgument >= 0) await LocalEngineChecks.CheckSpeakersAsync(root, args[speakerArgument + 1]);
    if (args.Contains("--ollama")) await LocalEngineChecks.CheckOllamaAsync();
    await ReliabilityChecks.RunAsync();
    await SecurityChecks.RunAsync();
    await ErrorHandlingChecks.RunAsync(root);
    var exportArgument = Array.IndexOf(args, "--export-openapi");
    if (exportArgument >= 0) await ReliabilityChecks.ExportOpenApiAsync(args[exportArgument + 1]);
    var contractArgument = Array.IndexOf(args, "--openapi");
    if (contractArgument >= 0)
        CheckOpenApi(args[contractArgument + 1]);
    if (args.Contains("--sql"))
    {
        var factory = new SqlTestFactory();
        await CheckSqlAsync(factory);
    }
    Console.WriteLine("All checks passed (domain, durable repository, upload limits, processing, failures, retry, WAV timing).");
}
finally { Directory.Delete(root, true); }

static void Check(bool result, string name) { if (!result) throw new Exception(name); }
static void CheckOpenApi(string path)
{
    var contract = File.ReadAllText(path);
    var parsed = OpenApiDocument.Parse(contract, "json");
    Check(parsed.Document is not null && parsed.Diagnostic?.Errors.Count == 0, "Valid OpenAPI document");
    using var json = JsonDocument.Parse(contract);
    var root = json.RootElement;
    var paths = root.GetProperty("paths");
    var expected = new Dictionary<string, string>
    {
        ["/health"] = "get", ["/api/meetings"] = "post", ["/api/meetings/{id}"] = "get",
        ["/api/meetings/{id}/transcript"] = "get", ["/api/meetings/{id}/transcript.txt"] = "get",
        ["/api/meetings/{id}/retry"] = "post", ["/api/meetings/{id}/local-export"] = "get",
        ["/api/meetings/{id}/recording"] = "get", ["/api/meetings/{id}/local-export/confirm"] = "post",
        ["/api/meetings/{id}/summary"] = "post",
        ["/api/transcription/configuration"] = "get", ["/api/errors"] = "get"
    };
    Check(paths.EnumerateObject().Count() == expected.Count, "All documented routes");
    var names = new HashSet<string>();
    foreach (var (route, method) in expected)
    {
        var operation = paths.GetProperty(route).GetProperty(method);
        Check(names.Add(operation.GetProperty("operationId").GetString()!), "Unique operation IDs");
        Check(operation.GetProperty("security").GetArrayLength() == (route == "/health" ? 0 : 1), "Correct API-key security");
    }
    var upload = paths.GetProperty("/api/meetings").GetProperty("post");
    Check(upload.GetProperty("requestBody").GetProperty("content").GetProperty("application/octet-stream")
        .GetProperty("schema").GetProperty("format").GetString() == "binary", "Raw binary upload contract");
    Check(upload.GetProperty("parameters").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "fileName")
        .GetProperty("required").GetBoolean(), "Filename query required");
    Check(upload.GetProperty("responses").GetProperty("202").GetProperty("headers").TryGetProperty("Location", out _), "Polling Location header");
    var schemas = root.GetProperty("components").GetProperty("schemas");
    Check(schemas.GetProperty("JobStatus").GetProperty("type").GetString() == "string", "String enum status");
    Check(schemas.GetProperty("ApiProblem").GetProperty("properties").TryGetProperty("code", out _)
        && upload.GetProperty("responses").GetProperty("415").GetProperty("content").TryGetProperty("application/problem+json", out _),
        "Controlled problem contract documented");
    Check(paths.GetProperty("/api/meetings/{id}/transcript.txt").GetProperty("get").GetProperty("responses")
        .GetProperty("409").GetProperty("content").TryGetProperty("application/json", out _), "Typed transcript conflict documented");
    Check(schemas.GetProperty("TranscriptSegment").GetProperty("properties").GetProperty("startSeconds")
        .GetProperty("type").GetString() == "number", "Numeric timestamps");
    Check(root.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey").GetProperty("name")
        .GetString() == "X-Api-Key", "Authentication header contract");
    Console.WriteLine("OpenAPI checks passed: document parser, routes, operation IDs, security, upload, response headers and schemas.");
}
static async Task CheckSqlAsync(IDbContextFactory<MeetingTranscriberDbContext> factory)
{
    var repository = new EfMeetingRepository(factory);
    var meeting = new Meeting { FileName = "sql-integration-test.wav", Language = "es", TemporaryExpiresAt = DateTimeOffset.UtcNow.AddHours(24) };
    try
    {
        await repository.SaveAsync(meeting, default);
        Check((await repository.ListAsync(default)).Any(x => x.Id == meeting.Id), "SQL queue");
        meeting.Start(2); meeting.Append([new(0, 1, "SQL test one", "chunk-0:A")]);
        await repository.SaveAsync(meeting, default);
        meeting.Append([new(600, 601, "SQL test two", "chunk-1:A")]); meeting.Complete();
        await repository.SaveAsync(meeting, default);
        await repository.SaveAsync(meeting, default);
        var restored = (await repository.GetAsync(meeting.Id, default))!;
        Check(restored.Status == JobStatus.Completed && restored.Segments.Count == 2, "SQL roundtrip / no duplicate segments");
        Check(restored.TemporaryExpiresAt == meeting.TemporaryExpiresAt, "SQL temporary expiration roundtrip");
        Check((await repository.ListExpiredAsync(DateTimeOffset.UtcNow.AddDays(2), default)).Contains(meeting.Id), "SQL expiry query");
        restored.Fail("IntegrationTest"); await repository.SaveAsync(restored, default);
        var retries = await Task.WhenAll(repository.TryRetryAsync(meeting.Id, default), repository.TryRetryAsync(meeting.Id, default));
        Check(retries.Count(x => x) == 1, "SQL atomic retry");
        var retried = (await repository.GetAsync(meeting.Id, default))!;
        Check(retried.Status == JobStatus.Queued && retried.Segments.Count == 0, "SQL retry clears segments");
        Check(retried.RetryCount == 1, "SQL retry count persisted");
        var errorLog = new EfErrorLog(factory, Microsoft.Extensions.Logging.Abstractions.NullLogger<EfErrorLog>.Instance);
        await errorLog.RecordAsync(new ErrorLogEntry { Source = ErrorSource.Transcription, Code = FailureCode.ProviderQuotaExceeded,
            MeetingId = meeting.Id, CorrelationId = "sql-test", ProviderStatus = 429, ProviderCode = "credit_balance_exhausted", Attempts = 1 });
        Check((await errorLog.ListAsync(meeting.Id, "sql-test", 20, default)).Single().ProviderCode == "credit_balance_exhausted",
            "SQL error persistence and filtering");
        for (var retry = 1; retry < 3; retry++)
        {
            retried.Fail("Test"); await repository.SaveAsync(retried, default);
            Check(await repository.TryRetryAsync(meeting.Id, default), "SQL retry within limit");
            retried = (await repository.GetAsync(meeting.Id, default))!;
        }
        retried.Fail("Test"); await repository.SaveAsync(retried, default);
        Check(!await repository.TryRetryAsync(meeting.Id, default), "SQL retry limit enforced");
        retried = (await repository.GetAsync(meeting.Id, default))!;
        // Requeue explicitly for the active-deletion check; the retry budget does not change here.
        retried.Restore(JobStatus.Queued, 0, 0, null, retried.RetryCount);
        await repository.SaveAsync(retried, default);
        Check(!await repository.DeleteFinalizedAsync(meeting.Id, default), "SQL active deletion denied");
        retried.Fail("Test"); await repository.SaveAsync(retried, default);
        Check(await repository.DeleteFinalizedAsync(meeting.Id, default), "SQL finalized deletion");
        Console.WriteLine("SQL Server integration checks passed.");
    }
    finally
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Meetings.Where(x => x.Id == meeting.Id).ExecuteDeleteAsync();
        await db.ErrorLogs.Where(x => x.MeetingId == meeting.Id).ExecuteDeleteAsync();
    }
}
static async Task Expect<T>(Func<Task> action) where T : Exception
{ try { await action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
sealed class FakePreprocessor : IAudioPreprocessor
{ public Task<IReadOnlyList<AudioChunk>> PrepareAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<AudioChunk>>(
    [new("unused", 0, 0, 600), new("unused", 1, 600, 30)]); }
sealed class FakeProvider : ITranscriptionProvider
{ public Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(AudioChunk chunk, string language, CancellationToken ct) =>
    Task.FromResult<IReadOnlyList<TranscriptSegment>>([new(1, 2, "Texto de prueba", "A")]); }
sealed class FailingProvider : ITranscriptionProvider
{ public Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(AudioChunk chunk, string language, CancellationToken ct) =>
    throw new Exception("Confidential vendor payload"); }
sealed class FakeDiarizer : ISpeakerDiarizer
{ public Task<IReadOnlyList<SpeakerTurn>> DiarizeAsync(Guid meetingId, CancellationToken ct) =>
    Task.FromResult<IReadOnlyList<SpeakerTurn>>([new(0, 1000, "Hablante 1")]); }
sealed class SqlTestFactory : IDbContextFactory<MeetingTranscriberDbContext>
{ public MeetingTranscriberDbContext CreateDbContext() => new MeetingTranscriberDbContextFactory().CreateDbContext([]); }

