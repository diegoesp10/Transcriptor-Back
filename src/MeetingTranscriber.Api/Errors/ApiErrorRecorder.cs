using MeetingTranscriber.Application;
using Microsoft.AspNetCore.Routing;

namespace MeetingTranscriber.Api.Errors;

public sealed class ApiErrorRecorder(IErrorLog errorLog, ILogger<ApiErrorRecorder> logger)
{
    public async Task RecordAsync(HttpContext context, Error error, Exception? exception)
    {
        if (!error.ShouldLog) return;
        var meetingId = Guid.TryParse(context.Request.RouteValues["id"]?.ToString(), out var id) ? id : (Guid?)null;
        var entry = exception is null
            ? new ErrorLogEntry { Source = error.Source, Code = error.Code, CorrelationId = context.TraceIdentifier }
            : ErrorLogEntry.FromException(exception, error.Source, context.TraceIdentifier, meetingId);
        try
        {
            await errorLog.RecordAsync(entry with
            {
                Code = error.Code, MeetingId = meetingId,
                HttpStatus = context.Response.StatusCode >= StatusCodes.Status400BadRequest
                    ? context.Response.StatusCode : ApiErrorPolicy.Status(error),
                Endpoint = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
            });
        }
        catch (Exception)
        {
            // Logging must never replace the original response or disclose a DB exception payload.
            logger.LogWarning("No se pudo registrar el error {Code} con correlación {CorrelationId}.",
                error.Code, context.TraceIdentifier);
        }
    }
}
