# Motores locales: instalación y funcionamiento

La aplicación utiliza **Whisper.net + pyannote Community-1 + Ollama**. Audio, vídeo y texto se procesan localmente. No se usan los adaptadores anteriores ni sus claves de proveedor. La clave `Security:ApiKey` sigue protegiendo tu propia API.

| Componente | Función | Requisito |
|---|---|---|
| Whisper.net / Whisper.net.Runtime 1.9.1 | Reconocer voz y tiempos | NuGet en Infrastructure y Visual C++ x64 |
| `ggml-large-v3-turbo.bin` | Modelo multilingüe de transcripción | Descarga de unos 1,62 GB |
| FFmpeg | Extraer audio de vídeos y normalizar formatos | Ejecutable configurado |
| Python estándar x64 + pyannote.audio 4.0.7 | Separar hablantes durante toda la reunión | Entorno aislado en tools/diarization/.venv |
| Community-1 | Modelo de diarización y detección de voz | Aceptar condiciones en Hugging Face y descargar |
| Ollama + qwen3:8b | Resumen, decisiones, tareas y preguntas | Servidor en localhost y modelo descargado (unos 5,2 GB) |

No hace falta un plugin de Codex ni otros paquetes NuGet para esta configuración CPU. Python 3.14 estándar se ha instalado y probado con las dependencias; no utilizar la variante free-threaded. No hace falta reiniciar Windows. Una consola nueva recoge PATH; Visual Studio puede necesitar reiniciarse.

## Preparar Whisper

Desde la raíz de la solución:

```powershell
.\scripts\Download-WhisperModel.ps1
```

Descarga large-v3-turbo y comprueba su SHA-256 contra el archivo publicado. `small` sigue disponible como alternativa ligera:

```powershell
.\scripts\Download-WhisperModel.ps1 -Model small
# Para utilizarlo, configurar Transcription:WhisperModelPath como
# ../../tools/whisper/models/ggml-small.bin
```

El modelo mayor es la configuración predeterminada. La calidad depende de ruido, micrófonos, acentos y voces simultáneas; validar nombres, cifras, negaciones y atribuciones sobre grabaciones reales antes de utilizar actas definitivas.

## Preparar los hablantes

1. Instalar Python estándar x64. El script admite 3.12 y 3.14 y crea un entorno aislado; no instala paquetes en tu Python global.
2. Iniciar sesión y aceptar las condiciones de [Community-1](https://huggingface.co/pyannote/speaker-diarization-community-1). Este paso corresponde al titular de la cuenta.
3. Crear un [token de lectura en Hugging Face](https://huggingface.co/settings/tokens), con acceso al modelo aceptado. No pegarlo en el chat ni guardarlo en el repositorio.
4. Ejecutar:

```powershell
.\scripts\Install-LocalDiarization.ps1 -DownloadModel
# Si PATH no se actualizó o tienes varias versiones:
# .\scripts\Install-LocalDiarization.ps1 -PythonPath 'C:\Program Files\Python314\python.exe' -DownloadModel
```

El token se introduce oculto en la consola. Solo se usa durante la descarga. El script valida que el modelo pueda cargarse offline y crea `.ready`; la API requiere esa validación antes de admitir trabajos con diarización. No aceptamos condiciones ni registramos tu token por ti.

La inferencia usa el modelo descargado en `tools/diarization/models/community-1`, fuerza `HF_HUB_OFFLINE=1` y desactiva la telemetría de pyannote. El audio se entrega como tensor PCM en memoria: no necesita la variante shared de FFmpeg para torchcodec. El ejecutable FFmpeg del proyecto sigue convirtiendo los originales.

Por defecto se usa CPU, con entre 1 y 20 hablantes detectados. Ajustar `MinSpeakers`/`MaxSpeakers` si se conoce un rango más preciso. Para usar GPU en diarización se necesita PyTorch compatible con CUDA y configurar `Diarization:Device=cuda`; no cambiarlo con una instalación de PyTorch CPU.

## Preparar los resúmenes

```powershell
.\scripts\Start-LocalSummary.ps1 -PullModel
```

Reutiliza el servidor si ya está activo; en otro caso lo inicia oculto, en localhost, con cloud deshabilitado. Descarga `qwen3:8b`. También se puede ejecutar manualmente:

```powershell
$env:OLLAMA_NO_CLOUD = '1'
ollama serve
# En otra consola:
ollama pull qwen3:8b
```

La API consulta `/api/show` antes de enviar texto y rechaza modelos remotos. Solo acepta direcciones HTTP con IP de loopback; no sigue redirecciones ni utiliza proxy. Los resúmenes usan esquema JSON, temperatura cero, referencias a segmentos originales y procesamiento jerárquico para reuniones largas. Las referencias se validan, pero no certifican que el modelo no haya cometido errores semánticos. El resultado se entrega como borrador.

## Configuración y diagnóstico

Las rutas relativas se resuelven respecto al proyecto API. Configuración completa en `src/MeetingTranscriber.Api/appsettings.json`. No necesitas `Transcription:ApiKey`, `BaseUrl` ni modelos GPT; las opciones antiguas que permanezcan en User Secrets se ignoran.

`GET /api/transcription/configuration` con `X-Api-Key` muestra `modelAvailable`, `ffmpegAvailable`, `diarizationEnabled`, `diarizationAvailable`, `ready`, `errorCode`, `summaryReady` y `summaryErrorCode`, sin revelar rutas ni secretos. El estado de resumen es independiente del de transcripción.

La diarización está activada por defecto. Si falta Python o Community-1, la subida devuelve 503 con un código concreto. Desactivarla explícitamente permite transcribir sin etiquetas: `Transcription:Diarization:Enabled=false`. No se simulan hablantes cuando el motor falta.

## Flujo y precisión

FFmpeg normaliza la primera pista de audio de cualquier medio admitido a PCM mono/16 kHz. Pyannote analiza el WAV completo, produce turnos exclusivos y conserva identidades como `Hablante 1` y `Hablante 2` para toda la reunión. Whisper transcribe ventanas con dos segundos de contexto solapado; cada palabra se conserva únicamente en su ventana central para evitar duplicados. Las marcas por token son estimaciones del modelo, no alineación fonética certificada.

La asignación usa solapamiento temporal y conserva `speaker=null` en casos inciertos. Los segmentos consecutivos del mismo hablante se agrupan para facilitar la lectura. Con diarización activa se descarta texto fuera de los intervalos donde el modelo detectó voz. Una grabación sin segmentos reconocidos devuelve `NoAudio`. La diarización atribuye voces; no separa físicamente voces simultáneas ni descubre nombres reales. Las interrupciones y el ruido requieren revisión.

Los trabajos son secuenciales; se mantienen progreso, errores SQL, limpieza temporal y límite de reintentos. Los resúmenes admiten un único trabajo simultáneo y devuelven `LocalInferenceBusy` cuando el motor está ocupado. No se lanzan reintentos automáticos de inferencia.

## Verificación

```powershell
dotnet run --project tests/MeetingTranscriber.Checks -- --whisper --openapi docs/openapi.json
.\tools\diarization\.venv\Scripts\python.exe -m unittest discover -s tests/python
# Con Ollama activo y el modelo descargado:
dotnet run --project tests/MeetingTranscriber.Checks -- --ollama
```

La prueba nativa de Whisper usa silencio sintético; la opción `--speech <archivo>` comprueba una muestra sintética en español y admite un vídeo con esa misma muestra. Los tests de hablantes verifican identidad global, límites y atribución incierta. La opción `--speakers <archivo>` verifica el puente .NET, Community-1 y Whisper con una muestra controlada de dos voces en el orden A-B-A. La instalación local pasó esa prueba con 15 turnos de voz y 19 segmentos de transcripción, conservando la identidad al volver la primera voz. Esta comprobación no mide la precisión en reuniones reales, que debe evaluarse con grabaciones representativas. No se aplican migraciones en estas pruebas.

Fuentes: [Whisper.net](https://github.com/sandrohanea/whisper.net), [modelos GGML](https://huggingface.co/ggerganov/whisper.cpp), [Community-1 y uso offline](https://huggingface.co/pyannote/speaker-diarization-community-1), [pyannote.audio](https://github.com/pyannote/pyannote-audio), [salidas estructuradas Ollama](https://docs.ollama.com/capabilities/structured-outputs).
