using MeetingTranscriber.Api.Configuration;
using MeetingTranscriber.Api.Errors;

namespace MeetingTranscriber.Api.Middleware;

public static class ApiPipelineExtensions
{
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseMiddleware<RequestExceptionMiddleware>();
        app.UseStatusCodePages(async statusContext =>
        {
            var context = statusContext.HttpContext;
            if (ApiErrorPolicy.FromStatus(context.Response.StatusCode) is { } error)
                await context.RequestServices.GetRequiredService<ApiErrorResponder>().WriteAsync(context, error);
        });
        app.UseRouting();
        app.UseMiddleware<RequestSecurityMiddleware>();
        // Admission and rate limits run before CORS preflight and API-key hashing.
        app.UseRateLimiter();
        // Preflight requests must be handled before authentication/authorization.
        app.UseCors(CorsOptions.FrontendPolicy);
        app.UseAuthentication();
        app.UseAuthorization();
        if (!app.Environment.IsDevelopment()) app.UseHsts();
        app.UseMiddleware<HttpsRequirementMiddleware>();
        return app;
    }
}
