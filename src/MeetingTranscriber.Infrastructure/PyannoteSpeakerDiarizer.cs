using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MeetingTranscriber.Application;

namespace MeetingTranscriber.Infrastructure;

public sealed partial class PyannoteSpeakerDiarizer(IMediaStorage storage, TranscriptionOptions options) : ISpeakerDiarizer
{
    public bool Enabled => options.Diarization.Enabled;
    public async Task<IReadOnlyList<SpeakerTurn>> DiarizeAsync(Guid meetingId, CancellationToken ct)
    {
        var settings = options.Diarization;
        if (!settings.Enabled) return [];
        if (TranscriptionReadiness.CheckDiarization(options) is { } error) throw new TranscriptionException(error.Code);
        var work = storage.WorkPath(meetingId);
        var output = Path.Combine(work, "speakers.json");
        var start = new ProcessStartInfo(settings.PythonPath)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { settings.ScriptPath, "--audio", Path.Combine(work, "meeting.wav"),
            "--model", settings.ModelPath, "--output", output, "--device", settings.Device,
            "--min-speakers", settings.MinSpeakers.ToString(CultureInfo.InvariantCulture),
            "--max-speakers", settings.MaxSpeakers.ToString(CultureInfo.InvariantCulture) }) start.ArgumentList.Add(argument);
        // Inference is offline. A Hugging Face token is used only by the separate download script.
        start.Environment.Remove("HF_TOKEN");
        start.Environment["HF_HUB_OFFLINE"] = "1";
        start.Environment["HF_HUB_DISABLE_TELEMETRY"] = "1";
        start.Environment["PYANNOTE_METRICS_ENABLED"] = "0";
        Process launched;
        try { launched = Process.Start(start) ?? throw new TranscriptionException(FailureCode.DiarizationRuntimeUnavailable); }
        catch (Win32Exception) { throw new TranscriptionException(FailureCode.DiarizationRuntimeUnavailable); }
        using var process = launched;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(settings.TimeoutMinutes));
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); await Task.WhenAll(stderr, stdout); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (ct.IsCancellationRequested) throw;
            throw new TranscriptionException(FailureCode.LocalInferenceTimeout);
        }
        if (process.ExitCode != 0) throw new TranscriptionException(process.ExitCode switch
        { 10 => FailureCode.DiarizationRuntimeUnavailable, 11 => FailureCode.DiarizationModelMissing, _ => FailureCode.DiarizationFailed });
        if (!File.Exists(output) || new FileInfo(output).Length > 16_000_000) throw new TranscriptionException(FailureCode.DiarizationFailed);
        try
        {
            await using var stream = File.OpenRead(output);
            var turns = await JsonSerializer.DeserializeAsync<List<SpeakerTurn>>(stream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web), ct) ?? throw new JsonException();
            if (turns.Any(t => !double.IsFinite(t.StartSeconds) || !double.IsFinite(t.EndSeconds)
                || t.StartSeconds < 0 || t.EndSeconds <= t.StartSeconds || t.Speaker is null || !SpeakerLabel().IsMatch(t.Speaker)))
                throw new JsonException();
            return turns.OrderBy(t => t.StartSeconds).ToArray();
        }
        catch (JsonException) { throw new TranscriptionException(FailureCode.DiarizationFailed); }
    }

    [GeneratedRegex("^Hablante [1-9][0-9]{0,2}$")]
    private static partial Regex SpeakerLabel();
}
