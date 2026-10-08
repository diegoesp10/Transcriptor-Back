namespace MeetingTranscriber.Application;

public enum ErrorSource { Api, Transcription, Summary, Worker }
public enum FailureCode
{
    UnexpectedError, ProcessingFailed, WorkerInterrupted, WorkerPersistenceFailed, ApiRequestFailed,
    MissingProviderKey, FFmpegUnavailable, MediaProcessingTimeout, InvalidMediaOrMissingAudio,
    ChunkTooLarge, NoAudio, ProviderTimeout, ProviderUnavailable, ProviderAuthenticationFailed,
    ProviderAccessDenied, ProviderRateLimited, ProviderQuotaExceeded, ProviderSpendLimitExceeded,
    ProviderAudioTooLarge, ProviderInvalidRequest, ProviderModelUnavailable, ProviderInvalidResponse,
    SummaryUnavailable, SummaryInputTooLarge, SummaryInvalidResponse,
    WhisperModelMissing, WhisperRuntimeUnavailable, WhisperInferenceFailed,
    DiarizationRuntimeUnavailable, DiarizationModelMissing, DiarizationFailed,
    LocalInferenceTimeout, LocalInferenceBusy, SummaryModelMissing, SummaryModelNotLocal,
    // Append only: keep historical names and numeric identities stable for consumers.
    MeetingNotFound, RecordingNotFound, TranscriptNotCompleted, MeetingRetryLimitExceeded,
    MeetingNotFailedOrRetryLimitExceeded, LocalFilesNotSaved, MeetingStillActive,
    InvalidRequest, UnsupportedMediaType, UploadTooLarge, EmptyUpload, UnsupportedMediaFormat,
    FileNameTooLong, InvalidLanguage, TranscriptEmpty, RequestValidationFailed,
    AuthenticationRequired, AccessDenied, GlobalRequestLimited, ServerConcurrencyLimited,
    SecurityCapacityReached, RequestBurstLimited, RequestConcurrencyLimited,
    ClientTemporarilyBlocked, RequestRateLimited, HttpMethodNotAllowed,
    RequestBodyTooLarge, InvalidHttpRequest, HttpsRequired, ResourceNotFound
}

