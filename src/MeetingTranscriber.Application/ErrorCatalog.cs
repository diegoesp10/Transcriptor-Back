namespace MeetingTranscriber.Application;

public static class ErrorCatalog
{
    public static Error Get(FailureCode code)
    {
        if (!Enum.IsDefined(code)) throw new ArgumentOutOfRangeException(nameof(code));
        var kind = code switch
        {
            FailureCode.MeetingNotFound or FailureCode.RecordingNotFound or FailureCode.ResourceNotFound => ErrorKind.NotFound,
            FailureCode.TranscriptNotCompleted or FailureCode.MeetingRetryLimitExceeded
                or FailureCode.MeetingNotFailedOrRetryLimitExceeded or FailureCode.MeetingStillActive => ErrorKind.Conflict,
            FailureCode.InvalidRequest or FailureCode.EmptyUpload or FailureCode.UnsupportedMediaFormat
                or FailureCode.FileNameTooLong or FailureCode.InvalidLanguage or FailureCode.TranscriptEmpty
                or FailureCode.RequestValidationFailed or FailureCode.LocalFilesNotSaved
                or FailureCode.InvalidHttpRequest or FailureCode.HttpsRequired => ErrorKind.Validation,
            FailureCode.UnsupportedMediaType => ErrorKind.UnsupportedMediaType,
            FailureCode.UploadTooLarge or FailureCode.RequestBodyTooLarge or FailureCode.SummaryInputTooLarge
                or FailureCode.ChunkTooLarge or FailureCode.ProviderAudioTooLarge => ErrorKind.PayloadTooLarge,
            FailureCode.AuthenticationRequired => ErrorKind.Authentication,
            FailureCode.AccessDenied or FailureCode.ClientTemporarilyBlocked => ErrorKind.Forbidden,
            FailureCode.GlobalRequestLimited or FailureCode.ServerConcurrencyLimited or FailureCode.RequestBurstLimited
                or FailureCode.RequestConcurrencyLimited or FailureCode.RequestRateLimited => ErrorKind.RateLimited,
            FailureCode.HttpMethodNotAllowed => ErrorKind.MethodNotAllowed,
            FailureCode.SecurityCapacityReached or FailureCode.SummaryUnavailable or FailureCode.SummaryModelMissing
                or FailureCode.SummaryModelNotLocal or FailureCode.LocalInferenceBusy or FailureCode.WhisperModelMissing
                or FailureCode.WhisperRuntimeUnavailable or FailureCode.FFmpegUnavailable
                or FailureCode.DiarizationModelMissing or FailureCode.DiarizationRuntimeUnavailable
                or FailureCode.ProviderQuotaExceeded or FailureCode.ProviderSpendLimitExceeded
                or FailureCode.ProviderRateLimited => ErrorKind.Unavailable,
            FailureCode.ProviderTimeout or FailureCode.LocalInferenceTimeout or FailureCode.MediaProcessingTimeout => ErrorKind.Timeout,
            FailureCode.ProviderUnavailable or FailureCode.ProviderAuthenticationFailed or FailureCode.ProviderAccessDenied
                or FailureCode.ProviderInvalidRequest or FailureCode.ProviderModelUnavailable or FailureCode.ProviderInvalidResponse
                or FailureCode.SummaryInvalidResponse => ErrorKind.DependencyFailure,
            _ => ErrorKind.Unexpected
        };
        var source = code switch
        {
            FailureCode.SummaryUnavailable or FailureCode.SummaryInputTooLarge or FailureCode.SummaryInvalidResponse
                or FailureCode.SummaryModelMissing or FailureCode.SummaryModelNotLocal => ErrorSource.Summary,
            FailureCode.WorkerInterrupted or FailureCode.WorkerPersistenceFailed => ErrorSource.Worker,
            FailureCode.ProcessingFailed or FailureCode.MissingProviderKey or FailureCode.FFmpegUnavailable
                or FailureCode.MediaProcessingTimeout or FailureCode.InvalidMediaOrMissingAudio or FailureCode.ChunkTooLarge
                or FailureCode.NoAudio or FailureCode.ProviderTimeout or FailureCode.ProviderUnavailable
                or FailureCode.ProviderAuthenticationFailed or FailureCode.ProviderAccessDenied or FailureCode.ProviderRateLimited
                or FailureCode.ProviderQuotaExceeded or FailureCode.ProviderSpendLimitExceeded or FailureCode.ProviderAudioTooLarge
                or FailureCode.ProviderInvalidRequest or FailureCode.ProviderModelUnavailable or FailureCode.ProviderInvalidResponse
                or FailureCode.WhisperModelMissing or FailureCode.WhisperRuntimeUnavailable or FailureCode.WhisperInferenceFailed
                or FailureCode.DiarizationRuntimeUnavailable or FailureCode.DiarizationModelMissing or FailureCode.DiarizationFailed
                or FailureCode.LocalInferenceTimeout or FailureCode.LocalInferenceBusy => ErrorSource.Transcription,
            _ => ErrorSource.Api
        };
        var shouldLog = (kind is ErrorKind.Unexpected or ErrorKind.DependencyFailure or ErrorKind.Timeout or ErrorKind.Unavailable)
            && code != FailureCode.SecurityCapacityReached;
        return new(code, kind, source, shouldLog);
    }

    public static FailureCode Parse(string code) => Enum.TryParse<FailureCode>(code, out var known)
        && Enum.IsDefined(known) ? known : FailureCode.UnexpectedError;

    public static string Message(FailureCode code) => code switch
    {
        FailureCode.MeetingNotFound => "La reunión no existe.",
        FailureCode.RecordingNotFound => "La grabación ya no está disponible.",
        FailureCode.ResourceNotFound => "El recurso solicitado no existe.",
        FailureCode.TranscriptNotCompleted => "La transcripción todavía no ha finalizado.",
        FailureCode.MeetingRetryLimitExceeded => "La reunión ha alcanzado el límite de reintentos.",
        FailureCode.MeetingNotFailedOrRetryLimitExceeded => "La reunión debe estar fallida y disponer de reintentos.",
        FailureCode.LocalFilesNotSaved => "Confirma que los archivos se han guardado antes de eliminar la copia temporal.",
        FailureCode.MeetingStillActive => "La reunión sigue activa y no se puede eliminar.",
        FailureCode.InvalidRequest or FailureCode.RequestValidationFailed or FailureCode.InvalidHttpRequest => "La solicitud no es válida.",
        FailureCode.UnsupportedMediaType => "Envía el archivo como application/octet-stream.",
        FailureCode.UploadTooLarge or FailureCode.RequestBodyTooLarge => "La solicitud supera el tamaño permitido.",
        FailureCode.EmptyUpload => "El archivo está vacío.",
        FailureCode.UnsupportedMediaFormat => "El formato de audio o vídeo no está permitido.",
        FailureCode.FileNameTooLong => "El nombre del archivo es demasiado largo.",
        FailureCode.InvalidLanguage => "Usa un código de idioma ISO de dos letras, como es o en.",
        FailureCode.TranscriptEmpty => "La transcripción no contiene segmentos.",
        FailureCode.AuthenticationRequired => "Se requiere autenticación válida.",
        FailureCode.AccessDenied => "No tienes permiso para esta operación.",
        FailureCode.GlobalRequestLimited or FailureCode.ServerConcurrencyLimited or FailureCode.RequestBurstLimited
            or FailureCode.RequestConcurrencyLimited or FailureCode.RequestRateLimited => "Se ha alcanzado el límite de solicitudes. Espera antes de repetir.",
        FailureCode.SecurityCapacityReached => "El servidor ha alcanzado su capacidad de admisión temporal.",
        FailureCode.ClientTemporarilyBlocked => "Las solicitudes de esta dirección están bloqueadas temporalmente.",
        FailureCode.HttpMethodNotAllowed => "El método HTTP no está permitido.",
        FailureCode.HttpsRequired => "Esta operación requiere HTTPS.",
        FailureCode.ProviderQuotaExceeded => "El proveedor anterior no tenía cuota disponible. Este código se conserva en trabajos históricos.",
        FailureCode.ProviderSpendLimitExceeded => "El proveedor anterior alcanzó su límite de gasto. Este código se conserva en trabajos históricos.",
        FailureCode.ProviderRateLimited => "El proveedor ha limitado temporalmente las peticiones.",
        FailureCode.ProviderAuthenticationFailed => "El proveedor ha rechazado las credenciales configuradas.",
        FailureCode.ProviderAccessDenied => "La clave no tiene permisos para el endpoint o modelo solicitado.",
        FailureCode.ProviderModelUnavailable => "El modelo solicitado no está disponible para este proyecto.",
        FailureCode.ProviderInvalidRequest => "El proveedor ha rechazado los parámetros de la solicitud.",
        FailureCode.ProviderInvalidResponse or FailureCode.SummaryInvalidResponse => "El proveedor ha devuelto una respuesta incompatible.",
        FailureCode.ProviderTimeout => "El proveedor no ha respondido dentro del tiempo permitido.",
        FailureCode.ProviderUnavailable => "No se ha podido completar la comunicación con el proveedor.",
        FailureCode.MissingProviderKey => "El trabajo antiguo requería una clave del proveedor. Reintentar con el motor local.",
        FailureCode.SummaryUnavailable => "Ollama no está disponible. Ejecutar ollama serve.",
        FailureCode.SummaryModelMissing => "El modelo de resumen no está descargado en Ollama.",
        FailureCode.SummaryModelNotLocal => "El resumen requiere un modelo local y una dirección de bucle local.",
        FailureCode.WhisperModelMissing => "No se encuentra el archivo del modelo Whisper configurado.",
        FailureCode.WhisperRuntimeUnavailable => "No se puede cargar el runtime de Whisper.net. Revisar el paquete nativo y Visual C++ x64.",
        FailureCode.WhisperInferenceFailed => "Whisper no pudo procesar el audio. Revisar modelo y recursos locales.",
        FailureCode.DiarizationRuntimeUnavailable => "Falta el entorno Python de diarización. Ejecutar el script de instalación.",
        FailureCode.DiarizationModelMissing => "Falta descargar o validar el modelo local Community-1 de separación de hablantes.",
        FailureCode.DiarizationFailed => "No se pudo completar la separación de hablantes con el motor local.",
        FailureCode.LocalInferenceTimeout => "El procesamiento local superó el tiempo permitido.",
        FailureCode.LocalInferenceBusy => "El motor local está ocupado. Esperar antes de repetir la petición.",
        FailureCode.FFmpegUnavailable => "FFmpeg no está disponible en la ruta configurada.",
        FailureCode.InvalidMediaOrMissingAudio => "El archivo no contiene audio compatible o está dañado.",
        FailureCode.MediaProcessingTimeout => "La conversión de audio ha superado el tiempo permitido.",
        FailureCode.ChunkTooLarge or FailureCode.ProviderAudioTooLarge => "El fragmento de audio supera el tamaño permitido.",
        FailureCode.NoAudio => "No se han obtenido fragmentos de audio.",
        FailureCode.WorkerInterrupted => "El procesamiento se interrumpió. Se requiere un reintento explícito.",
        FailureCode.WorkerPersistenceFailed => "El worker no ha podido acceder a la persistencia.",
        FailureCode.SummaryInputTooLarge => "La transcripción supera el tamaño permitido para generar el resumen.",
        _ => "La operación ha fallado. Consultar el registro con el identificador de correlación."
    };
}

