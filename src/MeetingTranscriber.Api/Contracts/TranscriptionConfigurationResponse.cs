namespace MeetingTranscriber.Api.Contracts;

public sealed record TranscriptionConfigurationResponse(string Provider, string Model, bool ModelAvailable,
    bool FfmpegAvailable, bool DiarizationEnabled, bool DiarizationAvailable, bool Ready, string? ErrorCode,
    string SummaryProvider, string SummaryModel, bool SummaryReady, string? SummaryErrorCode);
