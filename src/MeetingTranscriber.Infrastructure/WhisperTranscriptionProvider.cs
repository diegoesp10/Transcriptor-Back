using MeetingTranscriber.Application;
using MeetingTranscriber.Domain;
using Whisper.net;

namespace MeetingTranscriber.Infrastructure;

public sealed class WhisperTranscriptionProvider(TranscriptionOptions options) : ITranscriptionProvider, IDisposable
{
    private readonly SemaphoreSlim gate = new(1);
    private WhisperFactory? factory;

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(AudioChunk chunk, string language, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(options.WhisperModelPath)) throw new TranscriptionException(FailureCode.WhisperModelMissing);
            factory ??= WhisperFactory.FromPath(options.WhisperModelPath, new WhisperFactoryOptions { UseGpu = false });
            await using var processor = factory.CreateBuilder().WithLanguage(language)
                .WithThreads(options.WhisperThreads).WithTokenTimestamps().WithMaxSegmentLength(1)
                .WithTemperature(0).WithNoSpeechThreshold(.6f).Build();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(options.InferenceTimeoutMinutes));
            await using var audio = File.OpenRead(chunk.Path);
            var result = new List<TranscriptSegment>();
            await foreach (var segment in processor.ProcessAsync(audio, timeout.Token))
            {
                var start = Math.Clamp(segment.Start.TotalSeconds, 0, chunk.DurationSeconds);
                var end = Math.Clamp(segment.End.TotalSeconds, start, chunk.DurationSeconds);
                var midpoint = (start + end) / 2;
                // Punctuation and some short tokens have equal timestamps: preserve their text.
                if (!string.IsNullOrWhiteSpace(segment.Text) && midpoint >= chunk.KeepFromSeconds
                    && midpoint < (chunk.KeepUntilSeconds ?? chunk.DurationSeconds))
                    result.Add(new(start, end, segment.Text, null));
            }
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new TranscriptionException(FailureCode.LocalInferenceTimeout); }
        catch (TranscriptionException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (DllNotFoundException) { throw new TranscriptionException(FailureCode.WhisperRuntimeUnavailable); }
        catch (Exception) { throw new TranscriptionException(FailureCode.WhisperInferenceFailed); }
        finally { gate.Release(); }
    }

    public void Dispose() { factory?.Dispose(); gate.Dispose(); }
}
