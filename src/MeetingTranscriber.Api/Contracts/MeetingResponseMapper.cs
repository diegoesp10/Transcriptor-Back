using MeetingTranscriber.Domain;
using MeetingTranscriber.Application;

namespace MeetingTranscriber.Api.Contracts;

internal static class MeetingResponseMapper
{
    public static MeetingStatusResponse ToStatusResponse(this Meeting meeting) => new(
        meeting.Id, meeting.FileName, meeting.Language, meeting.CreatedAt,
        meeting.Status, meeting.CompletedChunks, meeting.TotalChunks, meeting.ErrorCode, meeting.TemporaryExpiresAt,
        meeting.RetryCount, meeting.ErrorCode is null ? null : ErrorCatalog.Message(ErrorCatalog.Parse(meeting.ErrorCode)));

    public static TranscriptResponse ToTranscriptResponse(this Meeting meeting) => new(
        meeting.Id, meeting.Language, TranscriptTiming.Segment,
        meeting.Segments.Any(s => s.Speaker?.StartsWith("chunk-", StringComparison.Ordinal) == true) ? "chunk" : "meeting",
        meeting.Segments);
}
