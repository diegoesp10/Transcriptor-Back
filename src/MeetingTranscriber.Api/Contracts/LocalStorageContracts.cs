using System.ComponentModel;

namespace MeetingTranscriber.Api.Contracts;

public sealed record LocalArtifact(string Kind, string FileName, string ContentType, string? Url, string Method, bool Available);
public sealed record LocalExportResponse(Guid MeetingId, string FolderName, string Destination,
    DateTimeOffset? TemporaryExpiresAt, bool CanConfirmExport, IReadOnlyList<LocalArtifact> Artifacts);
public sealed record ConfirmLocalExportRequest(
    [property: Description("El frontal debe confirmar que ha guardado y cerrado los archivos locales antes de solicitar el borrado.")] bool FilesSaved);
