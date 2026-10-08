using System.ComponentModel.DataAnnotations;
using MeetingTranscriber.Api.Contracts;
using MeetingTranscriber.Api.Formatting;
using MeetingTranscriber.Api.Services;
using MeetingTranscriber.Application;
using MeetingTranscriber.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeetingTranscriber.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/meetings")]
[Tags("Meetings")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
public sealed class MeetingsController(
    MeetingService service,
    MeetingRequests requests) : ControllerBase
{
    [HttpPost(Name = "SubmitMeeting")]
    [Consumes("application/octet-stream")]
    [EndpointSummary("Subir un audio o vídeo para transcribir")]
    [EndpointDescription("Devuelve 202 cuando el archivo y el trabajo se han guardado. Consultar GetMeeting hasta Completed o Failed. Un error de procesamiento posterior se comunica en el estado del trabajo.")]
    [ProducesResponseType(typeof(MeetingStatusResponse), StatusCodes.Status202Accepted, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> SubmitAsync([FromQuery, Required] string fileName,
        [FromQuery] string? language, CancellationToken ct)
    {
        var meeting = await requests.SubmitAsync(Request, fileName, language, ct);
        return Accepted($"/api/meetings/{meeting.Id}", meeting.ToStatusResponse());
    }

    [HttpGet("{id:guid}", Name = "GetMeeting")]
    [EndpointSummary("Consultar estado y progreso de una reunión")]
    [EndpointDescription("Estados: Queued, Processing, Completed y Failed. El progreso corresponde a fragmentos completados. No devuelve el texto transcrito.")]
    [ProducesResponseType(typeof(MeetingStatusResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        var meeting = await service.GetRequiredAsync(id, ct);
        return Ok(meeting.ToStatusResponse());
    }

    [HttpGet("{id:guid}/transcript", Name = "GetTranscript")]
    [EndpointSummary("Obtener la transcripción en JSON")]
    [EndpointDescription("Disponible solo en Completed. Tiempos en segundos desde el inicio. La diarización local conserva etiquetas Hablante 1, Hablante 2 entre fragmentos; speaker null indica atribución incierta.")]
    [ProducesResponseType(typeof(TranscriptResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTranscriptAsync(Guid id, CancellationToken ct)
    {
        var meeting = await service.GetCompletedAsync(id, ct);
        return Ok(meeting.ToTranscriptResponse());
    }

    [HttpGet("{id:guid}/transcript.txt", Name = "DownloadTranscript")]
    [EndpointSummary("Descargar la transcripción como TXT")]
    [EndpointDescription("Devuelve un archivo UTF-8 con Content-Disposition: attachment. Disponible solo en Completed.")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/plain")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DownloadTranscriptAsync(Guid id, CancellationToken ct)
    {
        var meeting = await service.GetCompletedAsync(id, ct);
        return File(TranscriptTextFormatter.Format(meeting.Segments), "text/plain; charset=utf-8", $"{id}.txt");
    }

    [HttpPost("{id:guid}/retry", Name = "RetryMeeting")]
    [EndpointSummary("Reintentar una reunión fallida")]
    [EndpointDescription("Solo admite Failed. Borra los segmentos anteriores y reencola el procesamiento local completo, dentro del límite de reintentos. No requiere cuerpo.")]
    [ProducesResponseType(typeof(RetryResponse), StatusCodes.Status202Accepted, "application/json")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> RetryAsync(Guid id, CancellationToken ct)
    {
        await requests.RetryAsync(id, ct);
        return Accepted($"/api/meetings/{id}", new RetryResponse(id, JobStatus.Queued));
    }
}
