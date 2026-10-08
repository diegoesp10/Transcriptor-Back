using System.Diagnostics;

namespace MeetingTranscriber.Application;

public sealed record ErrorLogEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public required ErrorSource Source { get; init; }
    public required FailureCode Code { get; init; }
    public string Message => ErrorCatalog.Message(Code);
    public required string CorrelationId { get; init; }
    public Guid? MeetingId { get; init; }
    public string? Endpoint { get; init; }
    public int? HttpStatus { get; init; }
    public int? ProviderStatus { get; init; }
    public string? ProviderCode { get; init; }
    public string? ProviderRequestId { get; init; }
    public int Attempts { get; init; }
    public int? ChunkIndex { get; init; }
    public string? ExceptionType { get; init; }
    public string? StackTrace { get; init; }

    // Only method names are recorded: exception messages and source paths can contain private data.
    public static ErrorLogEntry FromException(Exception ex, ErrorSource source, string correlationId,
        Guid? meetingId = null, int? chunkIndex = null)
    {
        var provider = ex as TranscriptionException;
        return new()
        {
            Source = source, Code = ex is ErrorException known ? known.Error.Code : FailureCode.ProcessingFailed,
            CorrelationId = correlationId, MeetingId = meetingId, ChunkIndex = chunkIndex,
            ProviderStatus = provider?.ProviderStatus, ProviderCode = provider?.ProviderCode,
            ProviderRequestId = provider?.ProviderRequestId, Attempts = provider?.Attempts ?? 0,
            ExceptionType = ex.GetType().FullName,
            StackTrace = string.Join("\n", new StackTrace(ex, false).GetFrames().Take(12)
                .Select(frame => frame.GetMethod()).Where(method => method is not null)
                .Select(method => $"{method!.DeclaringType?.FullName}.{method.Name}"))
        };
    }
}

public interface IErrorLog
{
    // Implementations must keep a logging failure from hiding the original error.
    Task RecordAsync(ErrorLogEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<ErrorLogEntry>> ListAsync(Guid? meetingId, string? correlationId, int take, CancellationToken ct);
}
