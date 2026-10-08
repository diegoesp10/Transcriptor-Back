namespace MeetingTranscriber.Application;

public enum ErrorKind
{
    Validation, NotFound, Conflict, Authentication, Forbidden, RateLimited, Unavailable,
    Timeout, PayloadTooLarge, UnsupportedMediaType, MethodNotAllowed, DependencyFailure, Unexpected
}

/// <summary>A transport-independent failure whose message and policy come from the catalog.</summary>
public sealed record Error
{
    internal Error(FailureCode code, ErrorKind kind, ErrorSource source, bool shouldLog)
        => (Code, Kind, Source, ShouldLog) = (code, kind, source, shouldLog);
    public FailureCode Code { get; }
    public ErrorKind Kind { get; }
    public ErrorSource Source { get; internal init; }
    public bool ShouldLog { get; }
    public string Message => ErrorCatalog.Message(Code);
}

/// <summary>Carries a known failure across services without leaking private exception messages.</summary>
public class ErrorException(Error error) : Exception(error.Message)
{
    public ErrorException(FailureCode code) : this(ErrorCatalog.Get(code)) { }
    public Error Error { get; } = error;
}
