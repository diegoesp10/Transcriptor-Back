using MeetingTranscriber.Application;
using MeetingTranscriber.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MeetingTranscriber.Infrastructure;

public sealed class TranscriptionWorker(IMeetingRepository repository, ProcessMeeting processor,
    TemporaryMeetingCleanup cleanup, ILogger<TranscriptionWorker> logger, IErrorLog errorLog) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Interrupted jobs retain partial results and require an explicit retry.
        var recovered = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var jobs = await repository.ListAsync(stoppingToken);
                if (!recovered)
                {
                    foreach (var job in jobs.Where(j => j.Status == JobStatus.Processing))
                    {
                        var interrupted = await repository.GetAsync(job.Id, stoppingToken);
                        if (interrupted is null) continue;
                        interrupted.Fail(FailureCode.WorkerInterrupted.ToString());
                        await errorLog.RecordAsync(new ErrorLogEntry { Source = ErrorSource.Worker,
                            Code = FailureCode.WorkerInterrupted, CorrelationId = Guid.NewGuid().ToString("N"), MeetingId = job.Id });
                        await repository.SaveAsync(interrupted, stoppingToken);
                    }
                    recovered = true;
                }
                foreach (var id in await repository.ListExpiredAsync(DateTimeOffset.UtcNow, stoppingToken))
                    await cleanup.DeleteAsync(id, stoppingToken);
                await cleanup.SweepAbandonedAsync(stoppingToken);
                foreach (var job in jobs.Where(j => j.Status == JobStatus.Queued))
                {
                    await processor.ExecuteAsync(job.Id, stoppingToken);
                    logger.LogInformation("Job {JobId} processing finished; inspect job status for the result", job.Id);
                }
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                await errorLog.RecordAsync(ErrorLogEntry.FromException(ex, ErrorSource.Worker, Guid.NewGuid().ToString("N"))
                    with { Code = FailureCode.WorkerPersistenceFailed });
                try { await Task.Delay(5000, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
