using MeetingTranscriber.Application;

namespace MeetingTranscriber.Api.Errors;

/// <summary>The single translation boundary between application failures and the HTTP contract.</summary>
public static class ApiErrorPolicy
{
    // Translate empty framework responses (routing, method selection, Consumes filters) too.
    public static Error? FromStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => ErrorCatalog.Get(FailureCode.InvalidHttpRequest),
        StatusCodes.Status401Unauthorized => ErrorCatalog.Get(FailureCode.AuthenticationRequired),
        StatusCodes.Status403Forbidden => ErrorCatalog.Get(FailureCode.AccessDenied),
        StatusCodes.Status404NotFound => ErrorCatalog.Get(FailureCode.ResourceNotFound),
        StatusCodes.Status405MethodNotAllowed => ErrorCatalog.Get(FailureCode.HttpMethodNotAllowed),
        StatusCodes.Status413PayloadTooLarge => ErrorCatalog.Get(FailureCode.RequestBodyTooLarge),
        StatusCodes.Status415UnsupportedMediaType => ErrorCatalog.Get(FailureCode.UnsupportedMediaType),
        _ => null
    };

    public static int Status(Error error) => error.Kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Authentication => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        ErrorKind.Timeout => StatusCodes.Status504GatewayTimeout,
        ErrorKind.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
        ErrorKind.UnsupportedMediaType => StatusCodes.Status415UnsupportedMediaType,
        ErrorKind.MethodNotAllowed => StatusCodes.Status405MethodNotAllowed,
        ErrorKind.DependencyFailure => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status500InternalServerError
    };

    // Existing clients consume { error: code } for these business/readiness failures.
    public static bool UsesLegacyContract(Error error) => error.Code is
        FailureCode.TranscriptNotCompleted or FailureCode.MeetingRetryLimitExceeded
        or FailureCode.MeetingNotFailedOrRetryLimitExceeded or FailureCode.LocalFilesNotSaved
        or FailureCode.MeetingStillActive or FailureCode.WhisperModelMissing or FailureCode.FFmpegUnavailable
        or FailureCode.DiarizationRuntimeUnavailable or FailureCode.DiarizationModelMissing;
}
