using MeetingTranscriber.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MeetingTranscriber.Infrastructure.Persistence;

public sealed class EfErrorLog(IDbContextFactory<MeetingTranscriberDbContext> factory, ILogger<EfErrorLog> logger) : IErrorLog
{
    public async Task RecordAsync(ErrorLogEntry entry, CancellationToken ct = default)
    {
        logger.LogError("Failure {ErrorId}: {Code}; Source={Source}; Meeting={MeetingId}; Trace={TraceId}; ProviderStatus={Status}; ProviderCode={ProviderCode}; Attempts={Attempts}",
            entry.Id, entry.Code, entry.Source, entry.MeetingId, entry.CorrelationId, entry.ProviderStatus, entry.ProviderCode, entry.Attempts);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await using var db = await factory.CreateDbContextAsync(timeout.Token);
            db.ErrorLogs.Add(new ErrorLogEntity
            {
                Id = entry.Id, CreatedAt = entry.CreatedAt, Source = entry.Source, Code = entry.Code,
                Message = entry.Message, CorrelationId = Limit(entry.CorrelationId, 100)!, MeetingId = entry.MeetingId,
                Endpoint = Limit(entry.Endpoint, 300), HttpStatus = entry.HttpStatus, ProviderStatus = entry.ProviderStatus,
                ProviderCode = Limit(entry.ProviderCode, 120), ProviderRequestId = Limit(entry.ProviderRequestId, 120),
                Attempts = entry.Attempts, ChunkIndex = entry.ChunkIndex,
                ExceptionType = Limit(entry.ExceptionType, 300), StackTrace = Limit(entry.StackTrace, 4000)
            });
            await db.SaveChangesAsync(timeout.Token);
        }
        catch (Exception)
        {
            // The structured console record survives a DB outage or an unapplied migration.
            logger.LogWarning("Could not persist error {ErrorId}; see the structured log. Check DB connectivity and migrations.", entry.Id);
        }
    }

    public async Task<IReadOnlyList<ErrorLogEntry>> ListAsync(Guid? meetingId, string? correlationId, int take, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.ErrorLogs.AsNoTracking();
        if (meetingId is { } id) query = query.Where(x => x.MeetingId == id);
        if (!string.IsNullOrEmpty(correlationId)) query = query.Where(x => x.CorrelationId == correlationId);
        return await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(Math.Clamp(take, 1, 100))
            .Select(x => new ErrorLogEntry
            {
                Id = x.Id, CreatedAt = x.CreatedAt, Source = x.Source, Code = x.Code, CorrelationId = x.CorrelationId,
                MeetingId = x.MeetingId, Endpoint = x.Endpoint, HttpStatus = x.HttpStatus, ProviderStatus = x.ProviderStatus,
                ProviderCode = x.ProviderCode, ProviderRequestId = x.ProviderRequestId, Attempts = x.Attempts,
                ChunkIndex = x.ChunkIndex, ExceptionType = x.ExceptionType, StackTrace = x.StackTrace
            }).ToArrayAsync(ct);
    }

    private static string? Limit(string? value, int length) => value?.Length > length ? value[..length] : value;
}
