using MeetingTranscriber.Api.Contracts;
using MeetingTranscriber.Application;
using MeetingTranscriber.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeetingTranscriber.Api.Controllers;

[ApiController, Authorize, Route("api/meetings/{id:guid}")]
[Tags("Meeting artifacts")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class MeetingArtifactsController(MeetingService service, IMediaStorage storage,
    TemporaryMeetingCleanup cleanup) : ControllerBase
{
    [HttpGet("local-export", Name = "GetLocalExport")]
    [EndpointSummary("Obtener archivos y destino para la exportación a carpeta local")]
    [EndpointDescription("La carpeta y su permiso se gestionan en el navegador. Nunca enviar una ruta local al backend. Las nuevas reuniones se conservan temporalmente hasta exportación o vencimiento.")]
    [ProducesResponseType(typeof(LocalExportResponse), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        var meeting = await service.GetRequiredAsync(id, ct);
        var ready = meeting.Status == JobStatus.Completed;
        return Ok(new LocalExportResponse(id, $"meeting-{id:N}", "browser-local-folder", meeting.TemporaryExpiresAt,
            meeting.Status is JobStatus.Completed or JobStatus.Failed,
            [new("recording", $"recording{Path.GetExtension(meeting.FileName)}", "application/octet-stream",
                $"/api/meetings/{id}/recording", "GET", System.IO.File.Exists(storage.SourcePath(id))),
             new("transcript-json", "transcript.json", "application/json", $"/api/meetings/{id}/transcript", "GET", ready),
             new("transcript-text", "transcript.txt", "text/plain", $"/api/meetings/{id}/transcript.txt", "GET", ready),
             new("summary-json", "summary.json", "application/json", $"/api/meetings/{id}/summary", "POST",
                ready)]));
    }

    [HttpGet("recording", Name = "DownloadRecording")]
    [EndpointSummary("Descargar el original temporal por streaming")]
    [ProducesResponseType(typeof(byte[]), 200, "application/octet-stream")]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DownloadAsync(Guid id, CancellationToken ct)
    {
        var recording = await service.GetRecordingAsync(id, ct);
        return PhysicalFile(recording.Path, "application/octet-stream", recording.FileName);
    }

    [HttpPost("local-export/confirm", Name = "ConfirmLocalExport")]
    [EndpointSummary("Confirmar archivos guardados y eliminar la copia temporal del servidor")]
    [EndpointDescription("Elimina grabación y transcripción SQL de una reunión finalizada. El frontal debe guardar también el resumen que quiera conservar antes de llamar. Las copias de proveedor y backups no se eliminan con esta llamada.")]
    [ProducesResponseType(204), ProducesResponseType(400), ProducesResponseType(404), ProducesResponseType(409)]
    public async Task<IActionResult> ConfirmAsync(Guid id, [FromBody] ConfirmLocalExportRequest request, CancellationToken ct)
    {
        await cleanup.ConfirmAsync(id, request.FilesSaved, ct);
        return NoContent();
    }
}
