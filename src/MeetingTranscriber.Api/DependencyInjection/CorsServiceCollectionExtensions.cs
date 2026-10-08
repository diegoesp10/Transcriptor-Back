using MeetingTranscriber.Api.Configuration;
using MeetingTranscriber.Api.Authentication;

namespace MeetingTranscriber.Api.DependencyInjection;

public static class CorsServiceCollectionExtensions
{
    public static IServiceCollection AddFrontendCors(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new();
        services.AddCors(cors => cors.AddPolicy(CorsOptions.FrontendPolicy, policy =>
        {
            if (options.AllowedOrigins.Length == 0) return;
            policy.WithOrigins(options.AllowedOrigins)
                .WithMethods("GET", "POST")
                .WithHeaders("Content-Type", ApiKeyDefaults.HeaderName)
                .WithExposedHeaders("Location", "Content-Disposition", "Retry-After", "X-Correlation-ID");
        }));
        return services;
    }
}
