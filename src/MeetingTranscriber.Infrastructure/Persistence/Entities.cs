using MeetingTranscriber.Domain;
using MeetingTranscriber.Application;

namespace MeetingTranscriber.Infrastructure.Persistence;

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class ClientEntity : BaseEntity
{
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public string? ExternalReference { get; set; }
    public ICollection<CaseFileEntity> CaseFiles { get; set; } = [];
}

public sealed class CaseFileEntity : BaseEntity
{
    public Guid ClientId { get; set; }
    public ClientEntity Client { get; set; } = null!;
    public string Reference { get; set; } = "";
    public string Title { get; set; } = "";
    public bool IsArchived { get; set; }
    public ICollection<MeetingEntity> Meetings { get; set; } = [];
}

public sealed class MeetingEntity : BaseEntity
{
    public DateTimeOffset? TemporaryExpiresAt { get; set; }
    public Guid? CaseFileId { get; set; }
    public CaseFileEntity? CaseFile { get; set; }
    public string? Title { get; set; }
    public string FileName { get; set; } = "";
    public string Language { get; set; } = "es";
    public JobStatus Status { get; set; }
    public int CompletedChunks { get; set; }
    public int TotalChunks { get; set; }
    public string? ErrorCode { get; set; }
    public int RetryCount { get; set; }
    public ICollection<TranscriptSegmentEntity> Segments { get; set; } = [];
    public ICollection<MeetingParticipantEntity> Participants { get; set; } = [];
}

public sealed class MeetingParticipantEntity : BaseEntity
{
    public Guid MeetingId { get; set; }
    public MeetingEntity Meeting { get; set; } = null!;
    public string DisplayName { get; set; } = "";
    public string? Role { get; set; }
    // Optional reviewed mapping; provider labels do not establish identity.
    public string? SpeakerLabel { get; set; }
}

public sealed class TranscriptSegmentEntity : BaseEntity
{
    public Guid MeetingId { get; set; }
    public MeetingEntity Meeting { get; set; } = null!;
    public int Sequence { get; set; }
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public string Text { get; set; } = "";
    public string? Speaker { get; set; }
}

// Independent of media retention: keep diagnostic metadata after a temporary meeting is deleted.
public sealed class ErrorLogEntity : BaseEntity
{
    public ErrorSource Source { get; set; }
    public FailureCode Code { get; set; }
    public string Message { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public Guid? MeetingId { get; set; }
    public string? Endpoint { get; set; }
    public int? HttpStatus { get; set; }
    public int? ProviderStatus { get; set; }
    public string? ProviderCode { get; set; }
    public string? ProviderRequestId { get; set; }
    public int Attempts { get; set; }
    public int? ChunkIndex { get; set; }
    public string? ExceptionType { get; set; }
    public string? StackTrace { get; set; }
}
