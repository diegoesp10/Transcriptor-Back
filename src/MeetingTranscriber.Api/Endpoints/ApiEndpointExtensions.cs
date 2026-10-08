namespace MeetingTranscriber.Api.Endpoints;

public static class ApiEndpointExtensions
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        app.MapDocumentationEndpoints();
        app.MapControllers();
        return app;
    }
}
