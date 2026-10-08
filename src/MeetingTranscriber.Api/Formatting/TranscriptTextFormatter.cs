using System.Text;
using MeetingTranscriber.Domain;

namespace MeetingTranscriber.Api.Formatting;

internal static class TranscriptTextFormatter
{
    public static byte[] Format(IEnumerable<TranscriptSegment> segments)
    {
        var text = string.Join(Environment.NewLine, segments.Select(segment =>
            $"[{TimeSpan.FromSeconds(segment.StartSeconds):c} – {TimeSpan.FromSeconds(segment.EndSeconds):c}] {segment.Speaker ?? "Hablante sin determinar"}: {segment.Text}"));
        return Encoding.UTF8.GetBytes(text);
    }
}
