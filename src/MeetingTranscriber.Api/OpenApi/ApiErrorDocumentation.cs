using Microsoft.OpenApi;

namespace MeetingTranscriber.Api.OpenApi;

public static class ApiErrorDocumentation
{
    public static void Describe(OpenApiOperation operation, OpenApiDocument document)
    {
        operation.Responses!.TryAdd("500", new OpenApiResponse { Description = "Fallo interno controlado con código seguro y correlación." });
        operation.Responses.TryAdd("503", new OpenApiResponse { Description = "Capacidad de admisión temporal agotada. Respetar Retry-After cuando esté presente." });
        document.Components!.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        document.Components.Schemas.TryAdd("ApiProblem", new OpenApiSchema
        {
            Type = JsonSchemaType.Object, Required = new HashSet<string> { "status", "title", "code", "correlationId" },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["type"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
                ["title"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["code"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Código estable del catálogo de errores." },
                ["correlationId"] = new OpenApiSchema { Type = JsonSchemaType.String }
            }
        });
        foreach (var (status, value) in operation.Responses!)
        {
            if (!int.TryParse(status, out var code) || code < 400 || value is not OpenApiResponse response) continue;
            response.Headers ??= new Dictionary<string, IOpenApiHeader>();
            response.Headers["X-Correlation-ID"] = new OpenApiHeader
            {
                Description = "Identificador para consultar el registro de diagnóstico.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            };
            if (code is 403 or 429 or 503) response.Headers.TryAdd("Retry-After", new OpenApiHeader
            {
                Description = "Si está presente, segundos que esperar antes de repetir.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.Integer }
            });
            response.Content ??= new Dictionary<string, OpenApiMediaType>();
            response.Content["application/problem+json"] = new()
            { Schema = new OpenApiSchemaReference("ApiProblem", document) };
            if ((code == 409 && operation.OperationId is "GetTranscript" or "DownloadTranscript" or "GenerateMeetingSummary" or "RetryMeeting" or "ConfirmLocalExport")
                || (code == 400 && operation.OperationId == "ConfirmLocalExport"))
                response.Content["application/json"] = new()
                { Schema = new OpenApiSchemaReference("ErrorResponse", document) };
        }
    }
}
