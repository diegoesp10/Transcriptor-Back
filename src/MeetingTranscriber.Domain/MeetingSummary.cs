namespace MeetingTranscriber.Domain;

public sealed record SummaryPoint(string Text, IReadOnlyList<int> EvidenceSegmentIndices);
public sealed record MeetingSummary(string Overview, IReadOnlyList<SummaryPoint> Highlights,
    IReadOnlyList<SummaryPoint> Decisions, IReadOnlyList<SummaryPoint> ActionItems, IReadOnlyList<SummaryPoint> OpenQuestions);
