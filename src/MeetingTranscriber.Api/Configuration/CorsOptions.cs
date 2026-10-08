namespace MeetingTranscriber.Api.Configuration;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";
    public const string FrontendPolicy = "Frontend";
    public string[] AllowedOrigins { get; set; } = [];
}
