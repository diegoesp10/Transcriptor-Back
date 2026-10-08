using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using MeetingTranscriber.Api.Authentication;

namespace MeetingTranscriber.Api.OpenApi;

public sealed class ApiDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Meeting Transcriber API",
            Version = "v1",
            Description = "Transcripción asíncrona de reuniones en audio/vídeo. Subir el archivo binario, consultar estado y descargar el resultado. "
                + "Los endpoints /api requieren X-Api-Key; /health es público. Los motores de IA son locales y las etiquetas de hablantes se mantienen durante toda la reunión."
        };
        // Relative URL keeps the live document valid on any host; exported files can override the base URL.
        document.Servers = [new OpenApiServer { Url = "/", Description = "Servidor que publica este contrato" }];
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[ApiKeyDefaults.SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyDefaults.HeaderName,
            Description = "Clave de acceso a este backend. Los motores de IA se ejecutan localmente."
        };
        foreach (var (path, item) in document.Paths)
        {
            if (item.Operations is null) continue;
            foreach (var operation in item.Operations.Values)
            {
                operation.Security = path == "/health" ? [] : [new OpenApiSecurityRequirement
                { [new OpenApiSecuritySchemeReference(ApiKeyDefaults.SchemeName, document)] = [] }];
                operation.Responses ??= new OpenApiResponses();
                operation.Responses["403"] = new OpenApiResponse
                {
                    Description = "Acceso no permitido o IP bloqueada temporalmente por abuso. Problem Details incluye code y correlationId. Si hay Retry-After, esperar esos segundos.",
                    Content = new Dictionary<string, OpenApiMediaType>
                    { ["application/problem+json"] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.Object } } }
                };
                operation.Responses["429"] = new OpenApiResponse
                {
                    Description = "Límite por IP, endpoint, ráfaga o concurrencia alcanzado. Repetir rechazos puede activar un bloqueo temporal. Respetar Retry-After.",
                    Headers = new Dictionary<string, IOpenApiHeader>
                    { ["Retry-After"] = new OpenApiHeader { Description = "Segundos de espera antes de otra petición.",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.Integer } } }
                };
                if (operation.Parameters is not null)
                    foreach (var parameter in operation.Parameters)
                    {
                        if (parameter.Name == "id") parameter.Description = "ID UUID de la reunión devuelto al subir el archivo.";
                        if (parameter.Name == "fileName") parameter.Description = "Nombre original con extensión: .mp3, .wav, .m4a, .flac, .ogg, .mp4, .mov, .mkv, .webm o .aac.";
                        if (parameter.Name == "language")
                        {
                            parameter.Description = "Idioma ISO de dos letras. Si se omite o está vacío, se utiliza es.";
                            if (parameter.Schema is OpenApiSchema schema) schema.Default = JsonValue.Create("es");
                        }
                    }
                if (operation.OperationId == "SubmitMeeting")
                {
                    operation.RequestBody = new OpenApiRequestBody
                    {
                        Required = true,
                        Description = "Contenido binario del audio o vídeo. No usar JSON, base64 ni multipart/form-data. Límite configurable, 2.000.000.000 bytes por defecto.",
                        Content = new Dictionary<string, OpenApiMediaType>
                        { ["application/octet-stream"] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } } }
                    };
                }
                if (operation.OperationId is "SubmitMeeting" or "RetryMeeting")
                {
                    if (operation.Responses?.TryGetValue("202", out var accepted) == true && accepted is OpenApiResponse response)
                    {
                        response.Description = "Trabajo aceptado. Consultar la URL de Location para seguir el progreso.";
                        response.Headers = new Dictionary<string, IOpenApiHeader>
                        { ["Location"] = new OpenApiHeader { Description = "Ruta relativa /api/meetings/{id} para consultar el estado.",
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String } } };
                    }
                }
                if (operation.OperationId == "DownloadTranscript"
                    && operation.Responses?.TryGetValue("200", out var download) == true && download is OpenApiResponse fileResponse)
                    fileResponse.Headers = new Dictionary<string, IOpenApiHeader>
                    { ["Content-Disposition"] = new OpenApiHeader { Description = "attachment; filename={id}.txt",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String } } };
                if (operation.OperationId == "DownloadRecording"
                    && operation.Responses?.TryGetValue("200", out var recording) == true && recording is OpenApiResponse recordingResponse)
                    recordingResponse.Content = new Dictionary<string, OpenApiMediaType>
                    { ["application/octet-stream"] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } } };
                ApiErrorDocumentation.Describe(operation, document);
            }
        }
        return Task.CompletedTask;
    }
}
