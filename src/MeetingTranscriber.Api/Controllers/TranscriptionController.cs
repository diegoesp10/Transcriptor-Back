using MeetingTranscriber.Infrastructure;
using MeetingTranscriber.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeetingTranscriber.Api.Controllers;

[ApiController, Authorize, Route("api/transcription")]
[Tags("Transcription")]
public sealed class TranscriptionController(TranscriptionOptions options, OllamaMeetingSummaryProvider summary) : ControllerBase
{
    [HttpGet("configuration", Name = "GetTranscriptionConfiguration")]
    [EndpointSummary("Comprobar los motores locales de transcripción, hablantes y resumen")]
    [EndpointDescription("No devuelve secretos ni rutas. Ready comprueba archivos locales; SummaryReady consulta Ollama. La precisión requiere validar grabaciones reales.")]
    [ProducesResponseType(typeof(TranscriptionConfigurationResponse), 200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<TranscriptionConfigurationResponse>> GetAsync(CancellationToken ct)
    {
        var summaryError = await summary.CheckReadinessAsync(ct);
        var transcriptionError = TranscriptionReadiness.Check(options);
        return new TranscriptionConfigurationResponse("Whisper.net", Path.GetFileName(options.WhisperModelPath),
            System.IO.File.Exists(options.WhisperModelPath), TranscriptionReadiness.HasFfmpeg(options),
            options.Diarization.Enabled, TranscriptionReadiness.CheckDiarization(options) is null,
            transcriptionError is null, transcriptionError?.Code.ToString(),
            "Ollama (local)", options.LocalSummary.Model, summaryError is null, summaryError?.Code.ToString());
    }
}
