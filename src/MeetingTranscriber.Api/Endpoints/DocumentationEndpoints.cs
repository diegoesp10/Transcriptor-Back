using Scalar.AspNetCore;

namespace MeetingTranscriber.Api.Endpoints;

public static class DocumentationEndpoints
{
    public static WebApplication MapDocumentationEndpoints(this WebApplication app)
    {
        var endpoint = app.MapOpenApi("/openapi/{documentName}.json").ExcludeFromDescription();
        if (app.Environment.IsDevelopment())
        {
            endpoint.AllowAnonymous();
            app.MapScalarApiReference("/api-docs", options => options.WithTitle("Meeting Transcriber API"))
                .AllowAnonymous().ExcludeFromDescription();
            app.MapGet("/", () => Results.Redirect("api-docs"))
                .AllowAnonymous().ExcludeFromDescription();
        }
        return app;
    }
}
