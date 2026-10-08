using System.Net;

namespace MeetingTranscriber.Api.Security;

public static class ClientAddress
{
    // Only the socket address is trusted. Never partition by a caller-supplied forwarding header.
    public static string Get(HttpContext context) => Normalize(context.Connection.RemoteIpAddress);
    public static string Normalize(IPAddress? address) => address is null ? "unknown" :
        (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
}
