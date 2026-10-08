# Transcripción local

Proveedor: `WhisperTranscriptionProvider` mediante Whisper.net 1.9.1, con modelo multilingüe `ggml-large-v3-turbo.bin`. Separación de hablantes: `PyannoteSpeakerDiarizer` con Community-1 offline. Resúmenes: `OllamaMeetingSummaryProvider` con qwen3:8b local. La instalación detallada y las limitaciones están en [LOCAL-MODELS.md](LOCAL-MODELS.md).

Audio y vídeo pasan por el mismo flujo: FFmpeg extrae la primera pista de audio, normaliza PCM16 mono a 16 kHz, conserva un WAV completo para diarización y produce ventanas de transcripción con contexto solapado. El worker analiza voces globales, transcribe, asigna hablantes, guarda progreso y limpia los archivos de trabajo. El original y los resultados temporales siguen sujetos al flujo de exportación y retención.

Los resultados tienen `timing=segment` y `speakerScope=meeting`. Las etiquetas `Hablante 1`, `Hablante 2`, etc. sobreviven a los cortes y no identifican personas por nombre. `speaker=null` significa atribución incierta. Los resultados históricos con etiquetas `chunk-*` conservan `speakerScope=chunk`.

## Requisitos y estado

`GET /api/transcription/configuration` requiere `X-Api-Key`. Devuelve disponibilidad del modelo, FFmpeg y diarización, más el estado independiente del resumidor. No expone rutas ni secretos. `ready` comprueba archivos locales; una inferencia posterior aún puede fallar por modelo dañado, runtime o recursos insuficientes.

Subida y retry devuelven 503 si falta un requisito. Con diarización activada por defecto se exige Python, script, modelo y marcador de validación offline. No se desactiva automáticamente un componente ausente.

| Código | Acción |
|---|---|
| WhisperModelMissing | Descargar el modelo o corregir WhisperModelPath |
| WhisperRuntimeUnavailable | Revisar NuGet Whisper.net.Runtime y Visual C++ x64 |
| WhisperInferenceFailed | Revisar integridad del modelo y recursos locales |
| DiarizationRuntimeUnavailable | Ejecutar Install-LocalDiarization.ps1 |
| DiarizationModelMissing | Aceptar condiciones y ejecutar instalación con -DownloadModel |
| DiarizationFailed | Revisar modelo, dispositivo y dependencias |
| FFmpegUnavailable | Corregir FfmpegPath |
| InvalidMediaOrMissingAudio | Archivo dañado, formato incompatible o vídeo sin audio |
| NoAudio | No se obtuvieron segmentos de voz transcritos |
| LocalInferenceTimeout | Aumentar timeout o reducir carga del equipo |
| LocalInferenceBusy | Esperar antes de solicitar otro resumen |
| SummaryUnavailable | Ejecutar ollama serve |
| SummaryModelMissing | Descargar el modelo configurado en Ollama |
| SummaryModelNotLocal | Utilizar modelo descargado y endpoint loopback |
| SummaryInvalidResponse | El modelo no devolvió JSON o referencias válidas |

Los fallos se registran sin cuerpos de proveedor, contenido de reuniones ni credenciales. Ver [ERRORS-AND-LIMITS.md](ERRORS-AND-LIMITS.md). Los códigos de cuota de trabajos históricos se conservan para diagnóstico, pero los motores actuales no utilizan API de pago.
