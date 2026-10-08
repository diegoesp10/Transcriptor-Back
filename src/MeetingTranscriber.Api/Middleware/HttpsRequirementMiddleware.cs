using MeetingTranscriber.Api.Errors;
using MeetingTranscriber.Application;

namespace MeetingTranscriber.Api.Middleware;

public sealed class HttpsRequirementMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context, ApiErrorResponder responder)
    {
        if (!environment.IsDevelopment() && !context.Request.IsHttps)
        {
            await responder.WriteAsync(context, ErrorCatalog.Get(FailureCode.HttpsRequired));
            return;
        }
        await next(context);
    }
}
