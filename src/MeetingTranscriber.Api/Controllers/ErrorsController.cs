using System.ComponentModel.DataAnnotations;
using MeetingTranscriber.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeetingTranscriber.Api.Controllers;

[ApiController, Authorize, Route("api/errors")]
[Tags("Diagnostics")]
public sealed class ErrorsController(IErrorLog errorLog) : ControllerBase
{
    [HttpGet(Name = "GetErrors")]
    [EndpointSummary("Consultar errores recientes y sus identificadores de correlación")]
    [ProducesResponseType(typeof(IReadOnlyList<ErrorLogEntry>), 200)]
    [ProducesResponseType(400), ProducesResponseType(401), ProducesResponseType(429)]
    public async Task<IActionResult> GetAsync([FromQuery] Guid? meetingId, [FromQuery, StringLength(100)] string? correlationId,
        [FromQuery, Range(1, 100)] int take = 20, CancellationToken ct = default) =>
        Ok(await errorLog.ListAsync(meetingId, correlationId, take, ct));
}
