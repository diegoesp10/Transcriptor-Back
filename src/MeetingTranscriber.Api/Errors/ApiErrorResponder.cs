using System.Globalization;
using MeetingTranscriber.Api.Contracts;
using MeetingTranscriber.Application;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using CorsOptions = MeetingTranscriber.Api.Configuration.CorsOptions;

namespace MeetingTranscriber.Api.Errors;

public sealed record ApiErrorFeature(Error Error);

public sealed class ApiErrorResponder
{
    public IActionResult Result(Error error) => new ErrorActionResult(this, error);

    public async Task WriteAsync(HttpContext context, Error error, int retryAfterSeconds = 0)
    {
        context.Features.Set(new ApiErrorFeature(error));
        // Admission and authentication may reject before CORS middleware runs.
        var corsService = context.RequestServices.GetService<ICorsService>();
        var policyProvider = context.RequestServices.GetService<ICorsPolicyProvider>();
        if (corsService is not null && policyProvider is not null
            && await policyProvider.GetPolicyAsync(context, CorsOptions.FrontendPolicy) is { } policy)
            corsService.ApplyResult(corsService.EvaluatePolicy(context, policy), context.Response);
        context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
        if (retryAfterSeconds > 0) context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        var status = ApiErrorPolicy.Status(error);
        var result = ApiErrorPolicy.UsesLegacyContract(error)
            ? Results.Json(new ErrorResponse(error.Code.ToString()), statusCode: status)
            // MVC's problem writer can suppress client errors. Serialize the controlled contract directly.
            : Results.Json(new ProblemDetails
            {
                Type = "about:blank", Status = status, Title = error.Message,
                Extensions = new Dictionary<string, object?>
                {
                    ["code"] = error.Code.ToString(), ["correlationId"] = context.TraceIdentifier
                }
            }, statusCode: status, contentType: "application/problem+json");
        await result.ExecuteAsync(context);
    }

    private sealed class ErrorActionResult(ApiErrorResponder responder, Error error) : IActionResult
    {
        public Task ExecuteResultAsync(ActionContext context) => responder.WriteAsync(context.HttpContext, error);
    }
}
