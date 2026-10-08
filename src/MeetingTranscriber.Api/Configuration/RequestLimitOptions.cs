using System.ComponentModel.DataAnnotations;

namespace MeetingTranscriber.Api.Configuration;

public sealed class RequestLimitOptions
{
    public const string SectionName = "RequestLimits";
    [Range(1, 100000)] public int GlobalPerMinute { get; set; } = 600;
    [Range(1, 10000)] public int PerIpPerMinute { get; set; } = 120;
    [Range(1, 10000)] public int ReadPerMinute { get; set; } = 60;
    [Range(1, 1000)] public int WritePerMinute { get; set; } = 10;
    [Range(1, 100)] public int UploadPerMinute { get; set; } = 3;
    [Range(1, 100)] public int RetryPerMinute { get; set; } = 3;
    [Range(1, 100)] public int SummaryPerMinute { get; set; } = 2;
    [Range(1, 100)] public int MaxConcurrentRequests { get; set; } = 20;
    [Range(1, 100)] public int PerIpConcurrentRequests { get; set; } = 6;
    [Range(1, 100)] public int MaxConcurrentUploads { get; set; } = 2;
    [Range(1, 1000)] public int PerIpBurstRequests { get; set; } = 30;
    [Range(1, 60)] public int BurstWindowSeconds { get; set; } = 10;
    [Range(2, 100)] public int AuthenticationFailuresPerMinute { get; set; } = 5;
    [Range(2, 100)] public int UnknownRoutesPerMinute { get; set; } = 10;
    [Range(2, 100)] public int RateViolationsPerMinute { get; set; } = 3;
    [Range(10, 3600)] public int BlockSeconds { get; set; } = 120;
    [Range(10, 86400)] public int MaxBlockSeconds { get; set; } = 900;
    [Range(1, 100000)] public int MaxTrackedIps { get; set; } = 4096;
    [Range(1, 120)] public int IdleRetentionMinutes { get; set; } = 30;
    [Range(1024, 1048576)] public int MaxNonUploadBodyBytes { get; set; } = 16384;
    public Dictionary<string, int> EndpointPerMinute { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
