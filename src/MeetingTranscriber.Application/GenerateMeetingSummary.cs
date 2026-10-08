using MeetingTranscriber.Domain;

namespace MeetingTranscriber.Application;

public interface IMeetingSummaryProvider
{
    Task<MeetingSummary> GenerateAsync(IReadOnlyList<TranscriptSegment> segments, string language, CancellationToken ct);
}
public sealed class SummaryUnavailableException() : ErrorException(FailureCode.SummaryUnavailable);
public sealed class SummaryInputTooLargeException() : ErrorException(FailureCode.SummaryInputTooLarge);
public sealed class SummaryProviderException() : ErrorException(FailureCode.SummaryInvalidResponse);

public sealed class GenerateMeetingSummary(IMeetingSummaryProvider provider)
{
    public async Task<MeetingSummary> ExecuteAsync(Meeting meeting, CancellationToken ct)
    {
        if (meeting.Status != JobStatus.Completed) throw new ErrorException(FailureCode.TranscriptNotCompleted);
        if (meeting.Segments.Count == 0) throw new ErrorException(FailureCode.TranscriptEmpty);
        if (meeting.Segments.Sum(x => (long)x.Text.Length) > 500_000) throw new SummaryInputTooLargeException();
        var summary = await provider.GenerateAsync(meeting.Segments, meeting.Language, ct);
        if (string.IsNullOrWhiteSpace(summary.Overview) || summary.Highlights is null || summary.Decisions is null
            || summary.ActionItems is null || summary.OpenQuestions is null) throw new SummaryProviderException();
        var points = summary.Highlights.Concat(summary.Decisions).Concat(summary.ActionItems).Concat(summary.OpenQuestions);
        if (points.Any(p => p is null || string.IsNullOrWhiteSpace(p.Text) || p.EvidenceSegmentIndices is null || p.EvidenceSegmentIndices.Count == 0
            || p.EvidenceSegmentIndices.Any(i => i < 0 || i >= meeting.Segments.Count))) throw new SummaryProviderException();
        return summary;
    }
}
