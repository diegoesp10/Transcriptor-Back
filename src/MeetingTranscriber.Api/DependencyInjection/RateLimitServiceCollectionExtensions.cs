using System.Threading.RateLimiting;
using MeetingTranscriber.Api.Configuration;
using MeetingTranscriber.Api.Middleware;
using MeetingTranscriber.Api.Security;
using MeetingTranscriber.Api.Errors;
using MeetingTranscriber.Application;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeetingTranscriber.Api.DependencyInjection;

public static class RateLimitServiceCollectionExtensions
{
    public static IServiceCollection AddApiRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(RequestLimitOptions.SectionName).Get<RequestLimitOptions>() ?? new();
        System.ComponentModel.DataAnnotations.Validator.ValidateObject(settings, new(settings), validateAllProperties: true);
        if (settings.MaxBlockSeconds < settings.BlockSeconds) throw new InvalidOperationException("MaxBlockSeconds debe ser mayor o igual que BlockSeconds.");
        if (settings.EndpointPerMinute is null || settings.EndpointPerMinute.Count > 100
            || settings.EndpointPerMinute.Any(p => p.Key.Length > 200 || p.Value is < 1 or > 10000))
            throw new InvalidOperationException("EndpointPerMinute debe contener hasta 100 plantillas y límites entre 1 y 10000.");
        services.AddSingleton(settings);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<RequestAbuseGuard>();
        services.Configure<KestrelServerOptions>(server =>
        {
            server.AddServerHeader = false;
            server.Limits.MaxConcurrentConnections = 100;
            server.Limits.MaxRequestHeadersTotalSize = 16384;
            server.Limits.MaxRequestHeaderCount = 50;
            server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            server.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(60);
            server.Limits.MinRequestBodyDataRate = new MinDataRate(240, TimeSpan.FromSeconds(10));
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context => Window(
                    ClientAddress.Get(context), settings.PerIpPerMinute)),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                {
                    // Route templates, not IDs/query strings or attacker-supplied API keys, define endpoints.
                    var route = RequestEndpoint.Template(context);
                    if (route is null) return RateLimitPartition.GetNoLimiter("unmatched");
                    var caller = ClientAddress.Get(context);
                    var method = HttpMethods.IsHead(context.Request.Method) ? "GET" : context.Request.Method;
                    var limit = method == "GET" ? settings.ReadPerMinute : settings.WritePerMinute;
                    if (method == "POST")
                    {
                        if (route.Trim('/') == "api/meetings") limit = settings.UploadPerMinute;
                        else if (route.EndsWith("/retry", StringComparison.Ordinal)) limit = settings.RetryPerMinute;
                        else if (route.EndsWith("/summary", StringComparison.Ordinal)) limit = settings.SummaryPerMinute;
                    }
                    if (settings.EndpointPerMinute.TryGetValue($"{method} /{route.Trim('/')}", out var endpointLimit))
                        limit = endpointLimit;
                    return Window($"{caller}|{method}|{route}", limit);
                }));
            options.OnRejected = async (rejected, ct) =>
            {
                var context = rejected.HttpContext;
                var seconds = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay)
                    ? Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds)) : 60;
                // Global overload and concurrency are handled separately without penalizing clients.
                context.RequestServices.GetRequiredService<RequestAbuseGuard>().Record(ClientAddress.Get(context), AbuseSignal.RateViolation);
                // Never amplify a request flood by inserting one DB row per rejected request.
                await context.RequestServices.GetRequiredService<ApiErrorResponder>()
                    .WriteAsync(context, ErrorCatalog.Get(FailureCode.RequestRateLimited), seconds);
            };
        });
        return services;
    }

    private static RateLimitPartition<string> Window(string key, int permits) => RateLimitPartition.GetSlidingWindowLimiter(key,
        _ => new SlidingWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6, QueueLimit = 0 });
}
