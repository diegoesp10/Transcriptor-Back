using MeetingTranscriber.Api.Errors;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeetingTranscriber.Api.DependencyInjection;

public static class ErrorHandlingServiceCollectionExtensions
{
    public static IServiceCollection AddApiErrorHandling(this IServiceCollection services)
    {
        services.TryAddSingleton<ApiErrorResponder>();
        services.TryAddTransient<ApiErrorRecorder>();
        return services;
    }
}
