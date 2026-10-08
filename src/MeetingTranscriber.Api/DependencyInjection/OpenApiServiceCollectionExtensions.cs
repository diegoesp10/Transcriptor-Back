using MeetingTranscriber.Api.OpenApi;
using Microsoft.OpenApi;

namespace MeetingTranscriber.Api.DependencyInjection;

public static class OpenApiServiceCollectionExtensions
{
    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddOpenApi("v1", options =>
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
            options.AddDocumentTransformer<ApiDocumentTransformer>();
            options.AddSchemaTransformer<StringEnumSchemaTransformer>();
        });
        return services;
    }
}
