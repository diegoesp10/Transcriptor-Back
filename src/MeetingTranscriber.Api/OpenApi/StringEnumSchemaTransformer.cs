using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MeetingTranscriber.Api.OpenApi;

public sealed class StringEnumSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken ct)
    {
        if (context.JsonTypeInfo.Type.IsEnum) schema.Type = JsonSchemaType.String;
        return Task.CompletedTask;
    }
}
