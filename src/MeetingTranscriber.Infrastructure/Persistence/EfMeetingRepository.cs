using System.Data;
using MeetingTranscriber.Application;
using MeetingTranscriber.Domain;
using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Persistence;

// A fresh context per operation keeps the hosted worker safe and avoids tracking stale state.
public sealed class EfMeetingRepository(IDbContextFactory<MeetingTranscriberDbContext> factory) : IMeetingRepository
{
    public async Task<Meeting?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Meetings.AsNoTracking().Include(x => x.Segments).SingleOrDefaultAsync(x => x.Id == id, ct);
        return entity is null ? null : ToDomain(entity);
    }
    public async Task<IReadOnlyList<Meeting>> ListAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // Polling/recovery only need active jobs, not every historical transcript.
        var entities = await db.Meetings.AsNoTracking().Where(x => x.Status == JobStatus.Queued || x.Status == JobStatus.Processing)
            .OrderBy(x => x.CreatedAt).ToListAsync(ct);
        return entities.Select(ToDomain).ToArray();
    }
    public async Task SaveAsync(Meeting meeting, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Meetings.SingleOrDefaultAsync(x => x.Id == meeting.Id, ct);
        if (entity is null)
        {
            entity = new MeetingEntity { Id = meeting.Id, CreatedAt = meeting.CreatedAt,
                FileName = meeting.FileName, Language = meeting.Language, TemporaryExpiresAt = meeting.TemporaryExpiresAt };
            db.Meetings.Add(entity);
        }
        entity.Status = meeting.Status; entity.CompletedChunks = meeting.CompletedChunks;
        entity.TotalChunks = meeting.TotalChunks; entity.ErrorCode = meeting.ErrorCode;
        entity.RetryCount = meeting.RetryCount;
        var savedCount = await db.TranscriptSegments.CountAsync(x => x.MeetingId == meeting.Id, ct);
        if (savedCount > meeting.Segments.Count) throw new InvalidOperationException("Use TryRetryAsync to reset a transcript.");
        for (var index = savedCount; index < meeting.Segments.Count; index++)
        {
            var segment = meeting.Segments[index];
            db.TranscriptSegments.Add(new TranscriptSegmentEntity { MeetingId = meeting.Id, Sequence = index,
                StartSeconds = segment.StartSeconds, EndSeconds = segment.EndSeconds, Text = segment.Text, Speaker = segment.Speaker });
        }
        await db.SaveChangesAsync(ct);
    }
    public async Task<bool> TryRetryAsync(Guid id, CancellationToken ct, int maxRetries = 3)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var changed = await db.Meetings.Where(x => x.Id == id && x.Status == JobStatus.Failed && x.RetryCount < maxRetries).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.Status, JobStatus.Queued).SetProperty(x => x.CompletedChunks, 0)
            .SetProperty(x => x.RetryCount, x => x.RetryCount + 1)
            .SetProperty(x => x.TotalChunks, 0).SetProperty(x => x.ErrorCode, (string?)null)
            .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), ct);
        if (changed == 0) return false;
        await db.TranscriptSegments.Where(x => x.MeetingId == id).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
    public async Task<bool> DeleteFinalizedAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Meetings.Where(x => x.Id == id && (x.Status == JobStatus.Completed || x.Status == JobStatus.Failed))
            .ExecuteDeleteAsync(ct) == 1;
    }
    public async Task<IReadOnlyList<Guid>> ListExpiredAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Meetings.AsNoTracking().Where(x => x.TemporaryExpiresAt != null && x.TemporaryExpiresAt <= now
            && (x.Status == JobStatus.Completed || x.Status == JobStatus.Failed)).Select(x => x.Id).ToListAsync(ct);
    }
    private static Meeting ToDomain(MeetingEntity entity)
    {
        var meeting = new Meeting { Id = entity.Id, FileName = entity.FileName, Language = entity.Language,
            CreatedAt = entity.CreatedAt, TemporaryExpiresAt = entity.TemporaryExpiresAt, Segments = entity.Segments.OrderBy(x => x.Sequence)
                .Select(x => new TranscriptSegment(x.StartSeconds, x.EndSeconds, x.Text, x.Speaker)).ToList() };
        meeting.Restore(entity.Status, entity.CompletedChunks, entity.TotalChunks, entity.ErrorCode, entity.RetryCount);
        return meeting;
    }
}
