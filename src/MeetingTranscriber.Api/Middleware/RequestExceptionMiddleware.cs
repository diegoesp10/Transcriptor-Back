using MeetingTranscriber.Application;
using MeetingTranscriber.Api.Errors;

namespace MeetingTranscriber.Api.Middleware;

public sealed class RequestExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ApiErrorResponder responder, ApiErrorRecorder recorder)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
            return Task.CompletedTask;
        });
        Exception? failure = null;
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { return; }
        catch (Exception ex)
        {
            failure = ex;
            var error = ApiErrorClassifier.Classify(ex);
            context.Features.Set(new ApiErrorFeature(error));
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                await responder.WriteAsync(context, error);
            }
            else context.Abort();
        }
        var reported = context.Features.Get<ApiErrorFeature>()?.Error;
        if (reported is not null) await recorder.RecordAsync(context, reported, failure);
        else if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
            await recorder.RecordAsync(context, ErrorCatalog.Get(FailureCode.ApiRequestFailed), failure);
    }
}
