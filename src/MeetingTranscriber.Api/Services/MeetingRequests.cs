using MeetingTranscriber.Application;
using MeetingTranscriber.Domain;
using MeetingTranscriber.Infrastructure;
using Microsoft.Net.Http.Headers;

namespace MeetingTranscriber.Api.Services;

/// <summary>Coordinates HTTP upload admission and local engine readiness outside controllers.</summary>
public sealed class MeetingRequests(MeetingService meetings, TranscriptionOptions options)
{
    public Task<Meeting> SubmitAsync(HttpRequest request, string fileName, string? language, CancellationToken ct)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            || !contentType.MediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
            throw new ErrorException(FailureCode.UnsupportedMediaType);
        if (request.ContentLength > options.MaxUploadBytes) throw new ErrorException(FailureCode.UploadTooLarge);
        EnsureReady();
        return meetings.SubmitAsync(fileName, string.IsNullOrEmpty(language) ? "es" : language,
            request.Body, options.MaxUploadBytes, ct, options.TemporaryRetentionHours);
    }

    public async Task RetryAsync(Guid id, CancellationToken ct)
    {
        var meeting = await meetings.GetRequiredAsync(id, ct);
        MeetingService.EnsureRetryAllowed(meeting, options.MaxMeetingRetries);
        EnsureReady();
        await meetings.RetryAsync(meeting, options.MaxMeetingRetries, ct);
    }

    private void EnsureReady()
    {
        if (TranscriptionReadiness.Check(options) is { } error) throw new ErrorException(error);
    }
}
