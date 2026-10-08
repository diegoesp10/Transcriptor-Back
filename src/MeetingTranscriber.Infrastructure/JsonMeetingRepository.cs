using System.Text.Json;
using MeetingTranscriber.Application;
using MeetingTranscriber.Domain;

namespace MeetingTranscriber.Infrastructure;

// Durable local starter: one process only. Replace with EF Core for multiple workers.
public sealed class JsonMeetingRepository : IMeetingRepository
{
    private readonly string root;
    private readonly SemaphoreSlim gate = new(1);
    private sealed record Snapshot(Guid Id, string FileName, string Language, DateTimeOffset CreatedAt,
        JobStatus Status, int CompletedChunks, int TotalChunks, string? ErrorCode, List<TranscriptSegment> Segments,
        DateTimeOffset? TemporaryExpiresAt = null, int RetryCount = 0);
    public JsonMeetingRepository(TranscriptionOptions options)
    { root = Path.Combine(Path.GetFullPath(options.DataRoot), "jobs"); Directory.CreateDirectory(root); }
    public async Task<Meeting?> GetAsync(Guid id, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return await ReadAsync(Path.Combine(root, $"{id:N}.json"), ct); }
        finally { gate.Release(); }
    }
    private static async Task<Meeting?> ReadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        var data = await JsonSerializer.DeserializeAsync<Snapshot>(stream, cancellationToken: ct)
            ?? throw new InvalidDataException();
        var meeting = new Meeting { Id = data.Id, FileName = data.FileName, Language = data.Language,
            CreatedAt = data.CreatedAt, Segments = data.Segments, TemporaryExpiresAt = data.TemporaryExpiresAt };
        meeting.Restore(data.Status, data.CompletedChunks, data.TotalChunks, data.ErrorCode, data.RetryCount);
        return meeting;
    }
    public async Task<IReadOnlyList<Meeting>> ListAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var result = new List<Meeting>();
            foreach (var path in Directory.EnumerateFiles(root, "*.json"))
                if (await ReadAsync(path, ct) is { } meeting) result.Add(meeting);
            return result.OrderBy(m => m.CreatedAt).ToArray();
        }
        finally { gate.Release(); }
    }
    public async Task SaveAsync(Meeting meeting, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { await SaveCoreAsync(meeting, ct); }
        finally { gate.Release(); }
    }
    public async Task<bool> TryRetryAsync(Guid id, CancellationToken ct, int maxRetries = 3)
    {
        await gate.WaitAsync(ct);
        try
        {
            var meeting = await ReadAsync(Path.Combine(root, $"{id:N}.json"), ct);
            if (meeting?.Status != JobStatus.Failed || meeting.RetryCount >= maxRetries) return false;
            meeting.Retry(); await SaveCoreAsync(meeting, ct); return true;
        }
        finally { gate.Release(); }
    }
    private async Task SaveCoreAsync(Meeting meeting, CancellationToken ct)
    {
        var path = Path.Combine(root, $"{meeting.Id:N}.json");
        var temp = path + ".tmp";
        try
        {
            var data = new Snapshot(meeting.Id, meeting.FileName, meeting.Language, meeting.CreatedAt,
                meeting.Status, meeting.CompletedChunks, meeting.TotalChunks, meeting.ErrorCode, meeting.Segments, meeting.TemporaryExpiresAt, meeting.RetryCount);
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, data, cancellationToken: ct);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public async Task<bool> DeleteFinalizedAsync(Guid id, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var path = Path.Combine(root, $"{id:N}.json");
            var meeting = await ReadAsync(path, ct);
            if (meeting?.Status is not (JobStatus.Completed or JobStatus.Failed)) return false;
            File.Delete(path); return true;
        }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<Guid>> ListExpiredAsync(DateTimeOffset now, CancellationToken ct) =>
        (await ListAsync(ct)).Where(x => x.TemporaryExpiresAt <= now && x.Status is JobStatus.Completed or JobStatus.Failed)
            .Select(x => x.Id).ToArray();
}
