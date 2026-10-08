using MeetingTranscriber.Application;
using MeetingTranscriber.Infrastructure.Hosting;
using MeetingTranscriber.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingTranscriber.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureLayer(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("MeetingTranscriber");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("ConnectionStrings:MeetingTranscriber is required.");

        services.AddDbContextFactory<MeetingTranscriberDbContext>(options => options.UseSqlServer(connection));
        services.AddSingleton<IMeetingRepository, EfMeetingRepository>();
        services.AddSingleton<IErrorLog, EfErrorLog>();
        services.AddSingleton<IMediaStorage, LocalMediaStorage>();
        services.AddSingleton<IAudioPreprocessor, FfmpegPreprocessor>();
        services.AddSingleton<ITranscriptionProvider, WhisperTranscriptionProvider>();
        services.AddSingleton<ISpeakerDiarizer, PyannoteSpeakerDiarizer>();
        services.AddHttpClient<OllamaMeetingSummaryProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        services.AddTransient<IMeetingSummaryProvider>(provider => provider.GetRequiredService<OllamaMeetingSummaryProvider>());
        services.AddHostedService<SingleInstanceGuard>();
        services.AddHostedService<TranscriptionWorker>();
        return services;
    }
}
