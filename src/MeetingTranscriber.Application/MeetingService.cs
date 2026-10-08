using MeetingTranscriber.Domain;

namespace MeetingTranscriber.Application;

public sealed class MeetingService(IMeetingRepository repository, IMediaStorage storage)
{
    public async Task<Meeting> GetRequiredAsync(Guid id, CancellationToken ct) =>
        await repository.GetAsync(id, ct) ?? throw new ErrorException(FailureCode.MeetingNotFound);

    public async Task<Meeting> GetCompletedAsync(Guid id, CancellationToken ct)
    {
        var meeting = await GetRequiredAsync(id, ct);
        if (meeting.Status != JobStatus.Completed) throw new ErrorException(FailureCode.TranscriptNotCompleted);
        return meeting;
    }

    public async Task<(string Path, string FileName)> GetRecordingAsync(Guid id, CancellationToken ct)
    {
        var meeting = await GetRequiredAsync(id, ct);
        var path = storage.SourcePath(id);
        if (!File.Exists(path)) throw new ErrorException(FailureCode.RecordingNotFound);
        return (path, $"recording{Path.GetExtension(meeting.FileName)}");
    }

    public static void EnsureRetryAllowed(Meeting meeting, int maxRetries)
    {
        if (meeting.Status == JobStatus.Failed && meeting.RetryCount >= maxRetries)
            throw new ErrorException(FailureCode.MeetingRetryLimitExceeded);
        if (meeting.Status != JobStatus.Failed)
            throw new ErrorException(FailureCode.MeetingNotFailedOrRetryLimitExceeded);
    }

    public async Task RetryAsync(Meeting meeting, int maxRetries, CancellationToken ct)
    {
        EnsureRetryAllowed(meeting, maxRetries);
        if (!await repository.TryRetryAsync(meeting.Id, ct, maxRetries))
            throw new ErrorException(FailureCode.MeetingNotFailedOrRetryLimitExceeded);
    }

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".m4a", ".flac", ".ogg", ".mp4", ".mov", ".mkv", ".webm", ".aac" };
    public async Task<Meeting> SubmitAsync(string fileName, string language, Stream stream, long maxBytes, CancellationToken ct,
        int? temporaryRetentionHours = null)
    {
        if (!Extensions.Contains(Path.GetExtension(fileName))) throw new ErrorException(FailureCode.UnsupportedMediaFormat);
        if (Path.GetFileName(fileName).Length > 260) throw new ErrorException(FailureCode.FileNameTooLong);
        if (language.Length != 2 || !language.All(char.IsAsciiLetter)) throw new ErrorException(FailureCode.InvalidLanguage);
        var meeting = new Meeting { FileName = Path.GetFileName(fileName), Language = language.ToLowerInvariant(),
            TemporaryExpiresAt = temporaryRetentionHours is { } hours ? DateTimeOffset.UtcNow.AddHours(hours) : null };
        await storage.StoreAsync(meeting.Id, stream, maxBytes, ct);
        await repository.SaveAsync(meeting, ct);
        return meeting;
    }
}
