using Microsoft.Extensions.Hosting;

namespace MeetingTranscriber.Infrastructure.Hosting;

// The local media queue supports one worker process per data directory.
public sealed class SingleInstanceGuard(TranscriptionOptions options) : IHostedService, IDisposable
{
    private FileStream? instanceLock;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.DataRoot);
        instanceLock = new FileStream(Path.Combine(options.DataRoot, "instance.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        instanceLock?.Dispose();
        instanceLock = null;
    }
}
