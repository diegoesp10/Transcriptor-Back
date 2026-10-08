using MeetingTranscriber.Domain;

namespace MeetingTranscriber.Api.Contracts;

public sealed record MeetingSummaryResponse(Guid MeetingId, DateTimeOffset GeneratedAt, bool RequiresHumanReview, MeetingSummary Summary);
