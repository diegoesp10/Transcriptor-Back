namespace MeetingTranscriber.Application;

/// <summary>A safe, stable error code; never includes media content or provider payloads.</summary>
public sealed class TranscriptionException(FailureCode code, int? providerStatus = null,
    string? providerCode = null, string? providerRequestId = null, int attempts = 0,
    ErrorSource? source = null) : ErrorException(source is { } origin
        ? ErrorCatalog.Get(code) with { Source = origin } : ErrorCatalog.Get(code))
{
    public string Code => Error.Code.ToString();
    public int? ProviderStatus { get; } = providerStatus;
    public string? ProviderCode { get; } = providerCode;
    public string? ProviderRequestId { get; } = providerRequestId;
    public int Attempts { get; } = attempts;
}
