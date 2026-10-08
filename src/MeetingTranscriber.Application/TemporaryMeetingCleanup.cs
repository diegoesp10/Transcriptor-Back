namespace MeetingTranscriber.Application;

public sealed class TemporaryMeetingCleanup(IMeetingRepository repository, IMediaStorage storage)
{
    public async Task ConfirmAsync(Guid id, bool filesSaved, CancellationToken ct)
    {
        if (!filesSaved) throw new ErrorException(FailureCode.LocalFilesNotSaved);
        if (await repository.GetAsync(id, ct) is null) throw new ErrorException(FailureCode.MeetingNotFound);
        if (!await DeleteAsync(id, ct)) throw new ErrorException(FailureCode.MeetingStillActive);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        // Remove the DB row atomically before the media, so a concurrent retry cannot claim this job.
        if (!await repository.DeleteFinalizedAsync(id, ct)) return false;
        storage.Delete(id);
        return true;
    }
    public async Task SweepAbandonedAsync(CancellationToken ct)
    {
        foreach (var id in storage.GetAbandonedCandidates(DateTimeOffset.UtcNow.AddHours(-24)))
            if (await repository.GetAsync(id, ct) is null) storage.Delete(id);
    }
}
