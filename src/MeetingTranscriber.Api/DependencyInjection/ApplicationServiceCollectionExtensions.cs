using MeetingTranscriber.Application;

namespace MeetingTranscriber.Api.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        services.AddTransient<MeetingService>();
        services.AddTransient<ProcessMeeting>();
        services.AddTransient<TemporaryMeetingCleanup>();
        services.AddTransient<GenerateMeetingSummary>();
        return services;
    }
}
