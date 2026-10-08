using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using MeetingTranscriber.Api.Configuration;
using MeetingTranscriber.Api.Errors;
using MeetingTranscriber.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MeetingTranscriber.Api.Authentication;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<SecurityOptions> securityOptions,
    ApiErrorResponder responder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var header) || string.IsNullOrEmpty(header))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (header.Count != 1 || header[0]!.Length > 256)
            return Task.FromResult(AuthenticateResult.Fail(ErrorCatalog.Message(FailureCode.AuthenticationRequired)));

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(header.ToString()));
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(securityOptions.Value.ApiKey));
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            return Task.FromResult(AuthenticateResult.Fail(ErrorCatalog.Message(FailureCode.AuthenticationRequired)));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "api-client")], Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        responder.WriteAsync(Context, ErrorCatalog.Get(FailureCode.AuthenticationRequired));

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        responder.WriteAsync(Context, ErrorCatalog.Get(FailureCode.AccessDenied));
}
