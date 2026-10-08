namespace MeetingTranscriber.Application;

public sealed class ProcessMeeting(IMeetingRepository repository, IMediaStorage storage,
    IAudioPreprocessor preprocessor, ITranscriptionProvider provider, IErrorLog errorLog, ISpeakerDiarizer diarizer)
{
    public async Task ExecuteAsync(Guid id, CancellationToken ct)
    {
        var meeting = await repository.GetAsync(id, ct) ?? throw new InvalidOperationException();
        if (meeting.Status != Domain.JobStatus.Queued) return;
        using var activity = new System.Diagnostics.Activity("ProcessMeeting").Start();
        var correlationId = activity.TraceId.ToString();
        int? chunkIndex = null;
        try
        {
            var chunks = await preprocessor.PrepareAsync(id, ct);
            meeting.Start(chunks.Count);
            await repository.SaveAsync(meeting, ct);
            var turns = await diarizer.DiarizeAsync(id, ct);
            foreach (var chunk in chunks)
            {
                chunkIndex = chunk.Index;
                var segments = await provider.TranscribeAsync(chunk, meeting.Language, ct);
                meeting.Append(SpeakerAttribution.Apply(segments, turns, chunk.OffsetSeconds, diarizer.Enabled));
                await repository.SaveAsync(meeting, ct);
            }
            if (meeting.Segments.Count == 0) throw new TranscriptionException(FailureCode.NoAudio);
            meeting.Complete();
            await repository.SaveAsync(meeting, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            meeting.Fail(FailureCode.WorkerInterrupted.ToString());
            await errorLog.RecordAsync(new ErrorLogEntry { Source = ErrorSource.Transcription,
                Code = FailureCode.WorkerInterrupted, CorrelationId = correlationId, MeetingId = id, ChunkIndex = chunkIndex });
            await repository.SaveAsync(meeting, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            // Do not persist vendor payloads, local paths or confidential content in errors.
            meeting.Fail(ex is ErrorException failure ? failure.Error.Code.ToString() : FailureCode.ProcessingFailed.ToString());
            await errorLog.RecordAsync(ErrorLogEntry.FromException(ex, ErrorSource.Transcription, correlationId, id, chunkIndex));
            await repository.SaveAsync(meeting, CancellationToken.None);
        }
        finally { storage.CleanWork(id); }
    }
}
