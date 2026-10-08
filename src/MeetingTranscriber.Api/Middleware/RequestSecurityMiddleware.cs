using MeetingTranscriber.Api.Configuration;
using MeetingTranscriber.Api.Security;
using MeetingTranscriber.Api.Errors;
using MeetingTranscriber.Application;
using Microsoft.AspNetCore.Http.Features;

namespace MeetingTranscriber.Api.Middleware;

public sealed class RequestSecurityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, RequestAbuseGuard guard, RequestLimitOptions limits, ApiErrorResponder responder)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });
        var ip = ClientAddress.Get(context);
        var route = RequestEndpoint.Template(context);
        var upload = HttpMethods.IsPost(context.Request.Method) && route == "api/meetings";
        var rejected = guard.TryEnter(ip, upload, out var admission);
        if (rejected is not null) { await responder.WriteAsync(context, rejected.Error, rejected.RetryAfterSeconds); return; }
        using (admission)
        {
            if (HttpMethods.IsTrace(context.Request.Method) || HttpMethods.IsConnect(context.Request.Method))
            {
                guard.Record(ip, AbuseSignal.UnknownRoute);
                await responder.WriteAsync(context, ErrorCatalog.Get(FailureCode.HttpMethodNotAllowed));
                return;
            }
            if (!upload)
            {
                if (context.Request.ContentLength > limits.MaxNonUploadBodyBytes)
                {
                    await responder.WriteAsync(context, ErrorCatalog.Get(FailureCode.RequestBodyTooLarge));
                    return;
                }
                var bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = limits.MaxNonUploadBodyBytes;
            }
            await next(context);
            if (context.Response.StatusCode == 401) guard.Record(ip, AbuseSignal.Authentication);
            else if (context.GetEndpoint() is null && context.Response.StatusCode is 404 or 405)
                guard.Record(ip, AbuseSignal.UnknownRoute);
        }
    }

}
