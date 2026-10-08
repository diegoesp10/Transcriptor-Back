using MeetingTranscriber.Api.DependencyInjection;
using MeetingTranscriber.Api.Endpoints;
using MeetingTranscriber.Api.Middleware;
using MeetingTranscriber.Infrastructure.DependencyInjection;

namespace MeetingTranscriber.Api;

public sealed class Startup(IConfiguration configuration, IWebHostEnvironment environment)
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddApiConfiguration(configuration, environment);
        services.AddApplicationLayer();
        services.AddInfrastructureLayer(configuration);
        services.AddApiLayer(configuration);
    }

    public void Configure(WebApplication app)
    {
        app.UseApiPipeline();
        app.MapApiEndpoints();
    }
}
