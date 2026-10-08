using MeetingTranscriber.Application;

namespace MeetingTranscriber.Infrastructure;

public sealed class UploadTooLargeException() : ErrorException(FailureCode.UploadTooLarge);
public sealed class LocalMediaStorage(TranscriptionOptions options) : IMediaStorage
{
    private readonly string root = Path.Combine(Path.GetFullPath(options.DataRoot), "media");
    public string SourcePath(Guid id) => Path.Combine(root, id.ToString("N"), "source");
    public string WorkPath(Guid id) => Path.Combine(root, id.ToString("N"), "work");
    public async Task StoreAsync(Guid id, Stream source, long maxBytes, CancellationToken ct)
    {
        var path = SourcePath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".upload";
        try
        {
            await using (var destination = File.Create(temp))
            {
                var buffer = new byte[81920]; long total = 0; int read;
                while ((read = await source.ReadAsync(buffer, ct)) != 0)
                {
                    total += read;
                    if (total > maxBytes) throw new UploadTooLargeException();
                    await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                if (total == 0) throw new ErrorException(FailureCode.EmptyUpload);
            }
            File.Move(temp, path);
        }
        catch { if (File.Exists(temp)) File.Delete(temp); throw; }
    }
    public void CleanWork(Guid id)
    {
        var path = WorkPath(id);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
    public void Delete(Guid id)
    {
        var path = Path.GetDirectoryName(SourcePath(id))!;
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
    public IReadOnlyList<Guid> GetAbandonedCandidates(DateTimeOffset olderThan)
    {
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateDirectories(root)
            .Where(path => Directory.GetLastWriteTimeUtc(path) < olderThan.UtcDateTime)
            .Select(Path.GetFileName).Where(name => Guid.TryParseExact(name, "N", out _))
            .Select(name => Guid.ParseExact(name!, "N")).ToArray();
    }
}
