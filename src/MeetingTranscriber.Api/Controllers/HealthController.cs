using MeetingTranscriber.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeetingTranscriber.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("health")]
[Tags("Health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet(Name = "GetHealth")]
    [EndpointSummary("Comprobar que la API responde")]
    [EndpointDescription("No requiere clave. No comprueba conectividad con SQL Server, FFmpeg ni el proveedor.")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK, "application/json")]
    public ActionResult<HealthResponse> Get() => Ok(new HealthResponse("ok"));
}
