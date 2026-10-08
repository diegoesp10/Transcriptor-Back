using System.ComponentModel;
using System.Text.Json.Serialization;
using MeetingTranscriber.Domain;

namespace MeetingTranscriber.Api.Contracts;

public sealed record HealthResponse(string Status);

public sealed record MeetingStatusResponse(
    Guid Id,
    string FileName,
    string Language,
    DateTimeOffset CreatedAt,
    JobStatus Status,
    int CompletedChunks,
    int TotalChunks,
    [property: Description("Código de fallo sin contenido confidencial; null cuando no hay error.")] string? ErrorCode,
    [property: Description("Caducidad de la copia temporal del servidor. El borrado se realiza al finalizar o confirmar la exportación local.")] DateTimeOffset? TemporaryExpiresAt,
    int RetryCount,
    [property: Description("Explicación controlada del fallo; nunca contiene el mensaje bruto del proveedor.")] string? ErrorMessage);

public enum TranscriptTiming
{
    [JsonStringEnumMemberName("chunk")] Chunk,
    [JsonStringEnumMemberName("segment")] Segment
}

public sealed record TranscriptResponse(
    Guid Id,
    string Language,
    [property: Description("chunk: tiempo aproximado por fragmento; segment: marcas de tiempo por segmento.")] TranscriptTiming Timing,
    [property: Description("meeting para diarización global local; chunk en resultados antiguos. Las etiquetas no identifican nombres reales.")] string SpeakerScope,
    IReadOnlyList<TranscriptSegment> Segments);

public sealed record RetryResponse(Guid Id, JobStatus Status);
public sealed record ErrorResponse(string Error);
