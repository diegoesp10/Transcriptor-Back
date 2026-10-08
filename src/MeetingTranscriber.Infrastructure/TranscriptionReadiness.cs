using MeetingTranscriber.Application;

namespace MeetingTranscriber.Infrastructure;

public static class TranscriptionReadiness
{
    public static bool HasFfmpeg(TranscriptionOptions options)
    {
        if (Path.IsPathRooted(options.FfmpegPath)) return File.Exists(options.FfmpegPath);
        var executable = OperatingSystem.IsWindows() && !options.FfmpegPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? options.FfmpegPath + ".exe" : options.FfmpegPath;
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Any(path => File.Exists(Path.Combine(path.Trim('"'), executable)));
    }

    public static Error? CheckDiarization(TranscriptionOptions options) => !options.Diarization.Enabled ? null :
        !File.Exists(options.Diarization.PythonPath) || !File.Exists(options.Diarization.ScriptPath)
            ? ErrorCatalog.Get(FailureCode.DiarizationRuntimeUnavailable) :
        !File.Exists(Path.Combine(options.Diarization.ModelPath, "config.yaml"))
            || !File.Exists(Path.Combine(options.Diarization.ModelPath, ".ready")) ? ErrorCatalog.Get(FailureCode.DiarizationModelMissing) : null;

    public static Error? Check(TranscriptionOptions options) =>
        !File.Exists(options.WhisperModelPath) ? ErrorCatalog.Get(FailureCode.WhisperModelMissing) :
        !HasFfmpeg(options) ? ErrorCatalog.Get(FailureCode.FFmpegUnavailable) : CheckDiarization(options);

    // String codes are serialized only at the configuration DTO boundary.
    public static string? DiarizationError(TranscriptionOptions options) => CheckDiarization(options)?.Code.ToString();
    public static string? ErrorCode(TranscriptionOptions options) => Check(options)?.Code.ToString();
}
