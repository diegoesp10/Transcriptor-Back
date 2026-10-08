using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace MeetingTranscriber.Api.Security;

public static class RequestEndpoint
{
    private static readonly object CacheKey = new();
    public static string? Template(HttpContext context)
    {
        if (context.Items.TryGetValue(CacheKey, out var cached)) return (string?)cached;
        var pattern = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern;
        // Public templates match OpenAPI and configuration keys: {id:guid} becomes {id}.
        var template = pattern is null ? null : string.Join('/', pattern.PathSegments.Select(segment =>
            string.Concat(segment.Parts.Select(part => part switch
            {
                RoutePatternLiteralPart literal => literal.Content,
                RoutePatternSeparatorPart separator => separator.Content,
                RoutePatternParameterPart parameter => "{" + parameter.Name + "}",
                _ => ""
            }))));
        context.Items[CacheKey] = template;
        return template;
    }
}
