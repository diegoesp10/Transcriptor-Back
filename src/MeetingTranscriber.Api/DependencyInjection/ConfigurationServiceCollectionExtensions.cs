using MeetingTranscriber.Api.Configuration;
using MeetingTranscriber.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace MeetingTranscriber.Api.DependencyInjection;

public static class ConfigurationServiceCollectionExtensions
{
    public static IServiceCollection AddApiConfiguration(this IServiceCollection services,
        IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => environment.IsDevelopment()
                || options.ApiKey != "meeting-transcriber-local-development-key", "Producción requiere una clave propia, distinta de la de desarrollo.")
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<TranscriptionOptions>, TranscriptionOptionsValidator>();
        services.AddOptions<TranscriptionOptions>()
            .Bind(configuration.GetSection("Transcription"))
            .PostConfigure(options =>
            {
                if (!string.IsNullOrWhiteSpace(options.DataRoot))
                    options.DataRoot = Path.GetFullPath(options.DataRoot, environment.ContentRootPath);
                if (options.FfmpegPath.Contains('/') || options.FfmpegPath.Contains('\\'))
                    options.FfmpegPath = Path.GetFullPath(options.FfmpegPath, environment.ContentRootPath);
                options.WhisperModelPath = Path.GetFullPath(options.WhisperModelPath, environment.ContentRootPath);
                options.Diarization.PythonPath = Path.GetFullPath(options.Diarization.PythonPath, environment.ContentRootPath);
                options.Diarization.ScriptPath = Path.GetFullPath(options.Diarization.ScriptPath, environment.ContentRootPath);
                options.Diarization.ModelPath = Path.GetFullPath(options.Diarization.ModelPath, environment.ContentRootPath);
            })
            .ValidateOnStart();

        // Existing infrastructure adapters use the immutable startup snapshot.
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<TranscriptionOptions>>().Value);
        services.AddOptions<KestrelServerOptions>()
            .Configure<IOptions<TranscriptionOptions>>((server, options) =>
                server.Limits.MaxRequestBodySize = options.Value.MaxUploadBytes);

        return services;
    }
}
