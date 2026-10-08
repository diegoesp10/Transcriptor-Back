using System.ComponentModel.DataAnnotations;

namespace MeetingTranscriber.Api.Configuration;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    [Required, MinLength(32), MaxLength(256)]
    public string ApiKey { get; set; } = "";
}
