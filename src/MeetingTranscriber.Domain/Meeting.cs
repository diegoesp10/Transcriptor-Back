namespace MeetingTranscriber.Domain;

public enum JobStatus { Queued, Processing, Completed, Failed }
public sealed record TranscriptSegment(double StartSeconds, double EndSeconds, string Text, string? Speaker);

public sealed class Meeting
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string FileName { get; init; }
    public required string Language { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? TemporaryExpiresAt { get; init; }
    public JobStatus Status { get; private set; } = JobStatus.Queued;
    public int CompletedChunks { get; private set; }
    public int TotalChunks { get; private set; }
    public string? ErrorCode { get; private set; }
    public int RetryCount { get; private set; }
    public List<TranscriptSegment> Segments { get; init; } = [];

    public void Start(int totalChunks)
    {
        if (Status != JobStatus.Queued || totalChunks < 1) throw new InvalidOperationException();
        TotalChunks = totalChunks;
        Status = JobStatus.Processing;
    }
    public void Append(IEnumerable<TranscriptSegment> segments)
    {
        if (Status != JobStatus.Processing || CompletedChunks >= TotalChunks) throw new InvalidOperationException();
        var batch = segments.ToArray();
        if (batch.Any(s => !double.IsFinite(s.StartSeconds) || !double.IsFinite(s.EndSeconds)
            || s.StartSeconds < 0 || s.EndSeconds < s.StartSeconds)) throw new InvalidDataException();
        Segments.AddRange(batch);
        CompletedChunks++;
    }
    public void Complete()
    {
        if (Status != JobStatus.Processing || CompletedChunks != TotalChunks) throw new InvalidOperationException();
        Status = JobStatus.Completed;
    }
    public void Fail(string code) { Status = JobStatus.Failed; ErrorCode = code; }
    public void Retry()
    {
        if (Status != JobStatus.Failed) throw new InvalidOperationException();
        RetryCount++;
        Status = JobStatus.Queued; ErrorCode = null; CompletedChunks = 0; TotalChunks = 0; Segments.Clear();
    }
    // Repository-only reconstruction; domain behavior still controls runtime transitions.
    public void Restore(JobStatus status, int completed, int total, string? error, int retryCount = 0)
    { Status = status; CompletedChunks = completed; TotalChunks = total; ErrorCode = error; RetryCount = retryCount; }
}
