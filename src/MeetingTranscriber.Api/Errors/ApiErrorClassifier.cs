using MeetingTranscriber.Application;

namespace MeetingTranscriber.Api.Errors;

public static class ApiErrorClassifier
{
    public static Error Classify(Exception exception) => exception switch
    {
        ErrorException known => known.Error,
        BadHttpRequestException badRequest => ErrorCatalog.Get(badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge
            ? FailureCode.RequestBodyTooLarge : FailureCode.InvalidHttpRequest),
        // Request validation uses typed failures or MVC's model-state factory. Untyped exceptions
        // can originate in persistence or programming defects and must not be mislabeled as client errors.
        HttpRequestException => ErrorCatalog.Get(FailureCode.ProviderUnavailable),
        OperationCanceledException => ErrorCatalog.Get(FailureCode.ProviderTimeout),
        _ => ErrorCatalog.Get(FailureCode.UnexpectedError)
    };
}
