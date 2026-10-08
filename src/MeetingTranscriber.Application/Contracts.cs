using MeetingTranscriber.Domain;

namespace MeetingTranscriber.Application;

public interface IMeetingRepository
{
    Task<Meeting?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Meeting>> ListAsync(CancellationToken ct);
    Task SaveAsync(Meeting meeting, CancellationToken ct);
    Task<bool> TryRetryAsync(Guid id, CancellationToken ct, int maxRetries = 3);
    Task<bool> DeleteFinalizedAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Guid>> ListExpiredAsync(DateTimeOffset now, CancellationToken ct);
}
public interface IMediaStorage
{
    Task StoreAsync(Guid id, Stream source, long maxBytes, CancellationToken ct);
    string SourcePath(Guid id);
    string WorkPath(Guid id);
    void CleanWork(Guid id);
    void Delete(Guid id);
    IReadOnlyList<Guid> GetAbandonedCandidates(DateTimeOffset olderThan);
}
public sealed record AudioChunk(string Path, int Index, double OffsetSeconds, double DurationSeconds,
    double KeepFromSeconds = 0, double? KeepUntilSeconds = null);
public interface IAudioPreprocessor
{
    Task<IReadOnlyList<AudioChunk>> PrepareAsync(Guid id, CancellationToken ct);
}
public interface ITranscriptionProvider
{
    Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(AudioChunk chunk, string language, CancellationToken ct);
}

public sealed record SpeakerTurn(double StartSeconds, double EndSeconds, string Speaker);
public interface ISpeakerDiarizer
{
    bool Enabled => true;
    Task<IReadOnlyList<SpeakerTurn>> DiarizeAsync(Guid meetingId, CancellationToken ct);
}
