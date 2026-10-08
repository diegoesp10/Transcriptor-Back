using System.Text;
using MeetingTranscriber.Domain;

namespace MeetingTranscriber.Application;

public static class SpeakerAttribution
{
    // Turns belong to the complete meeting, so identities survive transcription chunk boundaries.
    // Whisper supplies short timestamped pieces; never cut a sentence's text by time proportion.
    public static IReadOnlyList<TranscriptSegment> Apply(IEnumerable<TranscriptSegment> pieces,
        IReadOnlyList<SpeakerTurn> turns, double offsetSeconds, bool restrictToSpeech = false)
    {
        var result = new List<TranscriptSegment>();
        var text = new StringBuilder();
        TranscriptSegment? current = null;
        foreach (var piece in pieces)
        {
            if (string.IsNullOrWhiteSpace(piece.Text)) continue;
            var start = piece.StartSeconds + offsetSeconds;
            var end = piece.EndSeconds + offsetSeconds;
            var scores = turns.Where(t => t.StartSeconds < end && t.EndSeconds > start)
                .GroupBy(t => t.Speaker).Select(g => new { Speaker = g.Key,
                    Overlap = g.Sum(t => Math.Max(0, Math.Min(end, t.EndSeconds) - Math.Max(start, t.StartSeconds))) })
                .OrderByDescending(s => s.Overlap).Take(2).ToArray();
            // Retain uncertain edge tokens: ASR and voice activity timestamps can differ slightly.
            if (restrictToSpeech && scores.Length == 0
                && !turns.Any(t => start <= t.EndSeconds + .25 && end >= t.StartSeconds - .25)) continue;
            // Low coverage or ambiguous attribution is explicitly unknown, rather than invented.
            var speaker = scores.Length > 0 && scores[0].Overlap >= (end - start) * .5
                && (scores.Length == 1 || scores[0].Overlap > scores[1].Overlap * 1.5)
                ? scores[0].Speaker : null;
            if (current is not null && (current.Speaker != speaker || start - current.EndSeconds > .8
                || end - current.StartSeconds > 15 || text.Length + piece.Text.Length > 300)) Flush();
            current = current is null ? new(start, end, "", speaker) : current with { EndSeconds = Math.Max(current.EndSeconds, end) };
            text.Append(piece.Text);
        }
        Flush();
        return result;

        void Flush()
        {
            if (current is not null) result.Add(current with { Text = text.ToString().Trim() });
            current = null; text.Clear();
        }
    }
}
