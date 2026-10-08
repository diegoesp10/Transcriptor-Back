using MeetingTranscriber.Application;
using MeetingTranscriber.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeetingTranscriber.Api.Controllers;

[ApiController, Authorize, Route("api/meetings/{id:guid}/summary")]
[Tags("Meeting artifacts")]
public sealed class MeetingSummaryController(MeetingService service, GenerateMeetingSummary generator) : ControllerBase
{
    [HttpPost(Name = "GenerateMeetingSummary")]
    [EndpointSummary("Generar un borrador de resumen con puntos destacados y referencias")]
    [EndpointDescription("Requiere transcripción completada y Ollama con un modelo local descargado. Genera resumen, decisiones, tareas y preguntas con referencias a segmentos. No lo persiste. Límite: 500.000 caracteres; procesa reuniones largas por partes.")]
    [ProducesResponseType(typeof(MeetingSummaryResponse), 200)]
    [ProducesResponseType(401), ProducesResponseType(404), ProducesResponseType(409), ProducesResponseType(413)]
    [ProducesResponseType(502), ProducesResponseType(503)]
    [ProducesResponseType(504)]
    public async Task<IActionResult> GenerateAsync(Guid id, CancellationToken ct)
    {
        var meeting = await service.GetCompletedAsync(id, ct);
        var summary = await generator.ExecuteAsync(meeting, ct);
        return Ok(new MeetingSummaryResponse(id, DateTimeOffset.UtcNow, true, summary));
    }
}
