using System.Net;
using MeetingTranscriber.Infrastructure;
using Microsoft.Extensions.Options;

namespace MeetingTranscriber.Api.Configuration;

public sealed class TranscriptionOptionsValidator : IValidateOptions<TranscriptionOptions>
{
    public ValidateOptionsResult Validate(string? name, TranscriptionOptions options)
    {
        var errors = new List<string>();
        if (options.MaxMeetingRetries is < 0 or > 10) errors.Add("Transcription:MaxMeetingRetries must be between 0 and 10.");
        if (options.TemporaryRetentionHours is < 1 or > 168) errors.Add("Transcription:TemporaryRetentionHours must be between 1 and 168.");
        if (options.ChunkSeconds is < 30 or > 600) errors.Add("Transcription:ChunkSeconds must be between 30 and 600.");
        if (options.ChunkOverlapSeconds is < 0 or > 5) errors.Add("Transcription:ChunkOverlapSeconds must be between 0 and 5.");
        if (options.MaxUploadBytes <= 0) errors.Add("Transcription:MaxUploadBytes must be positive.");
        if (options.MediaTimeoutMinutes is < 1 or > 240 || options.InferenceTimeoutMinutes is < 1 or > 240)
            errors.Add("Media and inference timeouts must be between 1 and 240 minutes.");
        if (options.WhisperThreads is < 1 or > 64) errors.Add("Transcription:WhisperThreads must be between 1 and 64.");
        if (new[] { options.DataRoot, options.FfmpegPath, options.WhisperModelPath, options.Diarization.PythonPath,
            options.Diarization.ScriptPath, options.Diarization.ModelPath }.Any(string.IsNullOrWhiteSpace)) errors.Add("Local engine paths are required.");
        if (options.Diarization.Device is not ("cpu" or "cuda")) errors.Add("Diarization:Device must be cpu or cuda.");
        if (options.Diarization.MinSpeakers < 1 || options.Diarization.MaxSpeakers > 100
            || options.Diarization.MaxSpeakers < options.Diarization.MinSpeakers) errors.Add("Diarization speaker bounds must be between 1 and 100.");
        if (options.Diarization.TimeoutMinutes is < 1 or > 240) errors.Add("Diarization timeout must be between 1 and 240 minutes.");
        var summary = options.LocalSummary;
        if (!Uri.TryCreate(summary.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "http"
            || !IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) || !IPAddress.IsLoopback(address)
            || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != "")
            errors.Add("LocalSummary:BaseUrl must be an HTTP loopback IP address with root path, e.g. http://127.0.0.1:11434/.");
        if (string.IsNullOrWhiteSpace(summary.Model) || summary.Model.Contains("cloud", StringComparison.OrdinalIgnoreCase))
            errors.Add("LocalSummary:Model must name a local Ollama model.");
        if (summary.BatchCharacters is < 2000 or > 20000 || summary.ContextTokens is < 8192 or > 32768
            || summary.TimeoutMinutes is < 1 or > 120) errors.Add("LocalSummary batch, context or timeout is outside supported bounds.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
