using System.Text.Json.Serialization;
using MeetingTranscriber.Api.Authentication;
using MeetingTranscriber.Api.Errors;
using MeetingTranscriber.Api.Services;
using MeetingTranscriber.Application;

namespace MeetingTranscriber.Api.DependencyInjection;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApiLayer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddApiErrorHandling();
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            })
            .ConfigureApiBehaviorOptions(options =>
            {
                options.SuppressMapClientErrors = true;
                options.InvalidModelStateResponseFactory = context => context.HttpContext.RequestServices
                    .GetRequiredService<ApiErrorResponder>().Result(ErrorCatalog.Get(FailureCode.RequestValidationFailed));
            });
        services.AddTransient<MeetingRequests>();
        services.AddProblemDetails();
        services.AddApiKeyAuthentication();
        services.AddApiRateLimits(configuration);
        services.AddFrontendCors(configuration);
        services.AddApiDocumentation();
        return services;
    }
}
