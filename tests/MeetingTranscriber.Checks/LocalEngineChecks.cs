using System.Net;
using System.Text.Json;
using MeetingTranscriber.Application;
using MeetingTranscriber.Domain;
using MeetingTranscriber.Infrastructure;
using MeetingTranscriber.Api.Configuration;

static class LocalEngineChecks
{
    public static async Task CheckSpeakersAsync(string root, string path)
    {
        var options = new TranscriptionOptions { DataRoot = root,
            FfmpegPath = Path.GetFullPath("tools/ffmpeg/ffmpeg.exe"),
            WhisperModelPath = Path.GetFullPath("tools/whisper/models/ggml-large-v3-turbo.bin") };
        options.Diarization.PythonPath = Path.GetFullPath("tools/diarization/.venv/Scripts/python.exe");
        options.Diarization.ScriptPath = Path.GetFullPath("scripts/diarize.py");
        options.Diarization.ModelPath = Path.GetFullPath("tools/diarization/models/community-1");
        Check(TranscriptionReadiness.ErrorCode(options) is null, "Real local engines ready");
        var storage = new LocalMediaStorage(options);
        var id = Guid.NewGuid();
        await using (var input = File.OpenRead(path)) await storage.StoreAsync(id, input, 100_000_000, default);
        var chunks = await new FfmpegPreprocessor(storage, options).PrepareAsync(id, default);
        var turns = await new PyannoteSpeakerDiarizer(storage, options).DiarizeAsync(id, default);
        Check(turns.Select(t => t.Speaker).Distinct().Count() == 2
            && turns.First().Speaker == "Hablante 1" && turns.Last().Speaker == "Hablante 1"
            && turns.Any(t => t.Speaker == "Hablante 2"), "Real Community-1 two voices and returning speaker");
        using var whisper = new WhisperTranscriptionProvider(options);
        var transcript = new List<TranscriptSegment>();
        foreach (var chunk in chunks)
            transcript.AddRange(SpeakerAttribution.Apply(await whisper.TranscribeAsync(chunk, "es", default),
                turns, chunk.OffsetSeconds, true));
        Check(transcript.Where(s => s.Speaker is not null).Select(s => s.Speaker).Distinct().Count() == 2
            && string.Join(" ", transcript.Select(s => s.Text)).Contains("presupuesto", StringComparison.OrdinalIgnoreCase),
            "Real transcription text attributed to both voices through .NET bridge");
        Console.WriteLine($"Real Community-1 / Whisper integration passed: {turns.Count} turns, {transcript.Count} transcript segments, two stable speakers.");
        storage.Delete(id);
    }

    public static async Task CheckSpeechAsync(string root, string path)
    {
        var options = new TranscriptionOptions { DataRoot = root,
            FfmpegPath = Path.GetFullPath("tools/ffmpeg/ffmpeg.exe"),
            WhisperModelPath = Path.GetFullPath("tools/whisper/models/ggml-large-v3-turbo.bin") };
        options.Diarization.Enabled = false;
        var storage = new LocalMediaStorage(options);
        var id = Guid.NewGuid();
        await using (var original = File.OpenRead(path)) await storage.StoreAsync(id, original, 100_000_000, default);
        var chunks = await new FfmpegPreprocessor(storage, options).PrepareAsync(id, default);
        using var provider = new WhisperTranscriptionProvider(options);
        var pieces = await provider.TranscribeAsync(chunks.Single(), "es", default);
        var transcript = string.Join("", pieces.Select(s => s.Text));
        Console.WriteLine("Synthetic Spanish sample output: " + transcript.Trim());
        Check(transcript.Contains("presupuesto", StringComparison.OrdinalIgnoreCase)
            && (transcript.Contains("quinientos", StringComparison.OrdinalIgnoreCase) || transcript.Contains("500"))
            && transcript.Contains("mañana", StringComparison.OrdinalIgnoreCase), "Real Spanish audio/video transcription");
        Console.WriteLine("Real Spanish transcription passed on synthetic speech: " + transcript.Trim());
        storage.Delete(id);
    }

    public static async Task CheckOllamaAsync()
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
            { Timeout = Timeout.InfiniteTimeSpan };
        var provider = new OllamaMeetingSummaryProvider(client, new());
        var summary = await provider.GenerateAsync([new(0, 5, "Acordamos aprobar un presupuesto de quinientos euros.", "Hablante 1"),
            new(5, 10, "Diego enviará el informe mañana. Queda pendiente decidir la fecha de la próxima reunión.", "Hablante 2")], "es", default);
        Check(summary.Decisions.Count > 0 && summary.ActionItems.Count > 0
            && summary.Decisions.All(p => p.EvidenceSegmentIndices.Contains(0))
            && summary.ActionItems.All(p => p.EvidenceSegmentIndices.Contains(1)), "Real Ollama decisions, tasks and valid source references");
        Console.WriteLine("Real Ollama local summary passed (synthetic meeting, evidence verified).");
    }

    public static async Task RunAsync(string root, string wav, bool runWhisper)
    {
        await CheckChunkWindowsAsync(root);
        var turns = new SpeakerTurn[] { new(0, 2, "Hablante 1"), new(2, 4, "Hablante 2"), new(599, 603, "Hablante 1") };
        var attributed = SpeakerAttribution.Apply([new(0, 1, " Hola", null), new(1, 1.8, " Diego.", null),
            new(2.1, 3, " Buenos días.", null), new(5, 6, " Sin voz asignada.", null)], turns, 0);
        Check(attributed.Count == 3 && attributed[0].Text == "Hola Diego." && attributed[0].Speaker == "Hablante 1"
            && attributed[1].Speaker == "Hablante 2" && attributed[2].Speaker is null, "Clear turns / unknown speaker / punctuation");
        var acrossBoundary = SpeakerAttribution.Apply([new(0, 1, " Continúo.", null)], turns, 600);
        Check(acrossBoundary.Single().Speaker == "Hablante 1" && acrossBoundary[0].StartSeconds == 600, "Stable speaker across chunks");
        var ambiguous = SpeakerAttribution.Apply([new(1.5, 2.5, " Cambio", null)], turns, 0);
        Check(ambiguous.Single().Speaker is null, "Ambiguous speaker not invented");
        var speechOnly = SpeakerAttribution.Apply([new(4.1, 4.15, ".", null), new(10, 11, " Hallucination", null)], turns, 0, true);
        Check(speechOnly.Count == 1 && speechOnly[0].Speaker is null, "Retain uncertain edge punctuation / suppress text outside speech");

        var options = new TranscriptionOptions { WhisperModelPath = Path.Combine(root, "missing-model.bin") };
        using (var provider = new WhisperTranscriptionProvider(options))
        {
            await ExpectCode(() => provider.TranscribeAsync(new(wav, 0, 0, 1), "es", default), "WhisperModelMissing");
        }
        var validator = new TranscriptionOptionsValidator();
        Check(validator.Validate(null, new()).Succeeded, "Local defaults valid");
        options.LocalSummary.BaseUrl = "https://external.example/";
        Check(validator.Validate(null, options).Failed, "Remote summaries forbidden in configuration");
        var blocked = new OllamaHandler();
        using (var client = new HttpClient(blocked))
            await ExpectCode(() => new OllamaMeetingSummaryProvider(client, options).GenerateAsync([new(0, 1, "Prueba", null)], "es", default), "SummaryModelNotLocal");
        Check(blocked.ChatRequests == 0 && blocked.ShowRequests == 0, "Transcript never sent to a remote endpoint");

        options.LocalSummary.BaseUrl = "http://127.0.0.1:11434/";
        options.LocalSummary.BatchCharacters = 2000;
        var handler = new OllamaHandler();
        using (var client = new HttpClient(handler))
        {
            var provider = new OllamaMeetingSummaryProvider(client, options);
            Check(await provider.CheckReadinessAsync(default) is null, "Ollama local model readiness");
            var segments = Enumerable.Range(0, 24).Select(i => new TranscriptSegment(i, i + 1,
                new string('a', 220) + $" decisión {i}", "Hablante 1")).ToArray();
            var summary = await provider.GenerateAsync(segments, "es", default);
            Check(handler.ChatRequests > 2 && handler.ReduceRequests > 0 && summary.Highlights.Count > 1
                && summary.Highlights.All(p => p.EvidenceSegmentIndices.All(i => i >= 0 && i < segments.Length)),
                "Whole-meeting hierarchical summary / original evidence indices");
        }
        using (var client = new HttpClient(new OllamaHandler { Remote = true }))
            await ExpectCode(() => new OllamaMeetingSummaryProvider(client, options).GenerateAsync([new(0, 1, "Prueba", null)], "es", default), "SummaryModelNotLocal");
        using (var client = new HttpClient(new OllamaHandler { Missing = true }))
            Check((await new OllamaMeetingSummaryProvider(client, options).CheckReadinessAsync(default))?.Code == FailureCode.SummaryModelMissing, "Missing summary model diagnosed");
        using (var client = new HttpClient(new OllamaHandler { InvalidEvidence = true }))
        {
            try { await new OllamaMeetingSummaryProvider(client, options).GenerateAsync([new(0, 1, "Prueba", null)], "es", default); throw new Exception("Expected invalid citations"); }
            catch (SummaryProviderException) { }
        }
        var slow = new OllamaHandler { Hold = true };
        using (var client = new HttpClient(slow))
        {
            var provider = new OllamaMeetingSummaryProvider(client, options);
            var first = provider.GenerateAsync([new(0, 1, "Prueba", null)], "es", default);
            await slow.Started.Task;
            await ExpectCode(() => provider.GenerateAsync([new(0, 1, "Prueba", null)], "es", default), "LocalInferenceBusy");
            slow.Release.SetResult(); await first;
        }
        if (runWhisper)
        {
            options.WhisperModelPath = Path.GetFullPath("tools/whisper/models/ggml-small.bin");
            using var provider = new WhisperTranscriptionProvider(options);
            var segments = await provider.TranscribeAsync(new(wav, 0, 0, 1), "es", default);
            Check(segments.All(s => s.StartSeconds >= 0 && s.EndSeconds <= 1), "Real Whisper model/runtime load and timestamp bounds");
            Console.WriteLine("Real Whisper native inference passed (synthetic silence).");
        }
        Console.WriteLine("Local engine checks passed: global speakers, evidence, remote/cloud rejection, readiness and bounded inference concurrency.");
    }

    private static async Task CheckChunkWindowsAsync(string root)
    {
        var options = new TranscriptionOptions { DataRoot = root, ChunkSeconds = 30, ChunkOverlapSeconds = 2,
            FfmpegPath = Path.GetFullPath("tools/ffmpeg/ffmpeg.exe") };
        var wav = Path.Combine(root, "boundary-sample.wav");
        const int bytes = 65 * 32000;
        using (var writer = new BinaryWriter(File.Create(wav)))
        {
            writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000);
            writer.Write(32000); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8);
            writer.Write(bytes);
            for (var sample = 0; sample < bytes / 2; sample++) writer.Write((short)(sample / 16000));
        }
        var storage = new LocalMediaStorage(options);
        var id = Guid.NewGuid();
        await using (var input = File.OpenRead(wav)) await storage.StoreAsync(id, input, bytes + 100, default);
        var chunks = await new FfmpegPreprocessor(storage, options).PrepareAsync(id, default);
        Check(chunks.Count == 3 && chunks[0].OffsetSeconds == 0 && chunks[0].DurationSeconds == 32
            && chunks[1].OffsetSeconds == 28 && chunks[1].DurationSeconds == 34
            && chunks[2].OffsetSeconds == 58 && chunks[2].DurationSeconds == 7,
            "FFmpeg chunk boundary context and final short window");
        var coveredUntil = 0.0;
        foreach (var chunk in chunks)
        {
            Check(chunk.OffsetSeconds + chunk.KeepFromSeconds == coveredUntil, "No gaps or double ownership between chunks");
            coveredUntil = chunk.OffsetSeconds + chunk.KeepUntilSeconds!.Value;
            Check(FfmpegPreprocessor.WavDuration(chunk.Path) == chunk.DurationSeconds, "Copied PCM window duration");
            using var reader = new BinaryReader(File.OpenRead(chunk.Path));
            reader.BaseStream.Position = 44;
            Check(reader.ReadInt16() == (short)chunk.OffsetSeconds, "PCM copying starts at the expected source sample");
        }
        Check(coveredUntil == 65 && FfmpegPreprocessor.WavDuration(Path.Combine(storage.WorkPath(id), "meeting.wav")) == 65,
            "Full audio retained for global diarization / complete transcription coverage");
        storage.Delete(id);
    }

    private static void Check(bool result, string name) { if (!result) throw new Exception(name); }
    private static async Task ExpectCode(Func<Task> action, string code)
    {
        try { await action(); } catch (TranscriptionException ex) when (ex.Code == code) { return; }
        throw new Exception("Expected " + code);
    }

    private sealed class OllamaHandler : HttpMessageHandler
    {
        public bool Remote { get; init; }
        public bool Missing { get; init; }
        public bool InvalidEvidence { get; init; }
        public bool Hold { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ShowRequests { get; private set; }
        public int ChatRequests { get; private set; }
        public int ReduceRequests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.RequestUri!.Host == "127.0.0.1" && request.Headers.Authorization is null, "Local inference / no provider key");
            if (request.RequestUri.AbsolutePath == "/api/show")
            {
                ShowRequests++;
                return Missing ? new(HttpStatusCode.NotFound) : Reply(Remote ? new { remote_host = "https://ollama.com" } : (object)new { details = new { format = "gguf" } });
            }
            Check(request.RequestUri.AbsolutePath == "/api/chat", "Ollama chat endpoint");
            ChatRequests++;
            using var requestJson = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var body = requestJson.RootElement;
            Check(!body.GetProperty("stream").GetBoolean() && !body.GetProperty("think").GetBoolean()
                && body.GetProperty("format").GetProperty("type").GetString() == "object", "Structured local summary request");
            if (Hold) { Started.SetResult(); await Release.Task.WaitAsync(ct); }
            using var source = JsonDocument.Parse(body.GetProperty("messages")[1].GetProperty("content").GetString()!);
            int[] evidence;
            if (source.RootElement[0].TryGetProperty("index", out _))
                evidence = source.RootElement.EnumerateArray().Select(s => s.GetProperty("index").GetInt32()).Distinct().Take(1).ToArray();
            else
            {
                ReduceRequests++;
                evidence = source.RootElement.EnumerateArray().SelectMany(s => s.GetProperty("highlights").EnumerateArray())
                    .SelectMany(p => p.GetProperty("evidenceSegmentIndices").EnumerateArray()).Select(i => i.GetInt32()).Distinct().ToArray();
            }
            var summary = new MeetingSummary("Resumen de prueba", evidence.Select(i => new SummaryPoint("Punto " + i,
                [InvalidEvidence ? 99999 : i])).ToArray(), [], [], []);
            return Reply(new { done = true, done_reason = "stop", message = new { content = JsonSerializer.Serialize(summary, new JsonSerializerOptions(JsonSerializerDefaults.Web)) } });
        }
        private static HttpResponseMessage Reply(object data) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data)) };
    }
}
