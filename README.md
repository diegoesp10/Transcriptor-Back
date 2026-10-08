# Transcriptor de reuniones

Backend en **ASP.NET Core y .NET 10** para transcribir archivos de audio y vídeo, distinguir hablantes y generar resúmenes de reuniones. El procesamiento utiliza **Whisper.net**, **pyannote Community-1** y **Ollama** instalados localmente; no requiere una clave de OpenAI ni consume una API de IA de pago.

El repositorio incluye la API, persistencia SQL, procesamiento en segundo plano, scripts de instalación, documentación y pruebas. La interfaz de usuario y la captura del micrófono deben integrarse en un frontal independiente.

## Funcionalidades

- Subida de audio y vídeo por streaming, con tamaño máximo configurable.
- Cola persistente en SQL Server y consulta de estado y progreso.
- Transcripción multilingüe con marcas de tiempo y contexto entre fragmentos.
- Diarización de la reunión completa: etiquetas estables como `Hablante 1` y `Hablante 2`.
- Resúmenes con puntos destacados, decisiones, tareas, preguntas y referencias a segmentos originales.
- Descarga de transcripciones JSON/TXT y de la grabación original.
- Exportación a una carpeta elegida en el navegador mediante el módulo de integración incluido.
- Retención temporal, limpieza de trabajos finalizados y reintentos explícitos limitados.
- Errores tipados, registro de diagnóstico y correlación de peticiones.
- Autenticación mediante clave del backend, límites por IP y endpoint, y bloqueo temporal de abuso.
- Contrato OpenAPI e interfaz Scalar en desarrollo.

## Arquitectura

| Proyecto | Responsabilidad |
|---|---|
| `src/MeetingTranscriber.Domain` | Reuniones, estados, segmentos y estructuras de resumen. |
| `src/MeetingTranscriber.Application` | Casos de uso, reglas de negocio, errores tipados, atribución de hablantes y limpieza. |
| `src/MeetingTranscriber.Infrastructure` | EF Core y SQL Server, almacenamiento temporal, FFmpeg, Whisper.net, Python, Ollama y worker. |
| `src/MeetingTranscriber.Api` | Controladores, configuración, autenticación, seguridad, traducción de errores a HTTP y OpenAPI. |
| `tests/MeetingTranscriber.Checks` | Comprobaciones de servicios, persistencia, motores, errores HTTP, seguridad y contrato. |
| `tests/python` | Pruebas del puente de diarización. |

El dominio no depende de los motores de IA. Whisper.net y su runtime se referencian únicamente desde Infrastructure. Los controladores invocan servicios y construyen respuestas de éxito; el tratamiento de errores se realiza fuera de los endpoints.

## Requisitos

La instalación documentada está orientada a **Windows x64 y PowerShell**.

| Componente | Uso |
|---|---|
| .NET SDK 10 | Compilar y ejecutar la solución. |
| SQL Server accesible | Persistir trabajos, segmentos y registros de errores. |
| FFmpeg | Extraer el audio de vídeos y normalizar los formatos. |
| Microsoft Visual C++ Redistributable x64 | Cargar el runtime nativo de Whisper.net en Windows. |
| Python estándar x64 | Ejecutar pyannote en un entorno virtual propio. Python 3.14 se ha probado; también se admite 3.12. |
| Ollama | Ejecutar el modelo local de resumen. |
| Acceso a Internet durante la preparación | Restaurar paquetes y descargar modelos. |

No utilizar Python free-threaded para el entorno documentado. La configuración predeterminada usa CPU para Whisper y diarización; no exige GPU. Ollama gestiona los recursos del modelo de resumen según la instalación y el equipo. Los modelos consumen varios GB de disco y memoria; los tiempos de procesamiento dependen del hardware y de la duración del archivo.

## Instalación y primer arranque

Ejecutar los comandos desde la raíz del repositorio.

### 1. Restaurar la solución

```powershell
dotnet restore MeetingTranscriber.slnx
dotnet tool restore
```

Los paquetes NuGet necesarios ya están declarados en los proyectos. No hay que instalarlos manualmente en cada capa ni añadir un plugin de Codex.

### 2. Configurar FFmpeg

La configuración de Development espera este ejecutable:

```text
tools/ffmpeg/ffmpeg.exe
```

Colocar ahí una distribución de FFmpeg para Windows, junto con sus dependencias si las necesita. Si se utiliza otra ubicación, configurar `Transcription:FfmpegPath`. Para usar un ejecutable instalado en PATH durante la sesión:

```powershell
$env:Transcription__FfmpegPath = 'ffmpeg'
ffmpeg -version
```

### 3. Descargar el modelo de transcripción

```powershell
.\scripts\Download-WhisperModel.ps1
```

El script descarga y verifica mediante SHA-256 `ggml-large-v3-turbo.bin`, de aproximadamente 1,62 GB, en `tools/whisper/models/`. Es el modelo configurado por defecto.

Como alternativa más ligera:

```powershell
.\scripts\Download-WhisperModel.ps1 -Model small
$env:Transcription__WhisperModelPath = '../../tools/whisper/models/ggml-small.bin'
```

### 4. Preparar la separación de hablantes

1. Instalar Python estándar x64 y añadirlo a PATH.
2. Iniciar sesión en Hugging Face y aceptar las condiciones de [pyannote/speaker-diarization-community-1](https://huggingface.co/pyannote/speaker-diarization-community-1).
3. Crear un token de lectura con acceso al modelo en [Hugging Face](https://huggingface.co/settings/tokens).
4. Ejecutar:

```powershell
.\scripts\Install-LocalDiarization.ps1 -DownloadModel
```

El script crea `tools/diarization/.venv`, instala las dependencias fijadas del proyecto, solicita el token de forma oculta y descarga Community-1. Después valida la carga offline y crea el marcador `.ready`. No guarda el token en la configuración ni en el repositorio; la inferencia utiliza la copia local sin token.

Si Python no está en PATH o hay varias versiones, usar el parámetro `-PythonPath` con la ruta del ejecutable correcto. La diarización está activada por defecto: si faltan su entorno o su modelo, la API rechaza nuevos trabajos con un error controlado. Se puede desactivar expresamente con `Transcription:Diarization:Enabled=false`, en cuyo caso no se generan etiquetas de hablantes.

### 5. Preparar Ollama y el resumen

Con Ollama instalado:

```powershell
.\scripts\Start-LocalSummary.ps1 -PullModel
```

El script reutiliza el servidor local si está activo o lo inicia en `127.0.0.1:11434`, y descarga `qwen3:8b`, de aproximadamente 5,2 GB. El resumen se solicita después de completar la transcripción; Ollama no es necesario para transcribir.

Para gestionar el servidor manualmente:

```powershell
$env:OLLAMA_NO_CLOUD = '1'
$env:OLLAMA_HOST = '127.0.0.1:11434'
ollama serve
```

En otra consola:

```powershell
ollama pull qwen3:8b
```

La API solo admite un servidor de resumen en loopback, rechaza modelos remotos y no sigue redirecciones ni utiliza proxy para enviar transcripciones.

### 6. Configurar SQL Server y aplicar migraciones

Development utiliza esta conexión local con autenticación Windows:

```text
Server=localhost;Database=MeetingTranscriber;Integrated Security=True;TrustServerCertificate=True;Encrypt=False
```

La instancia debe existir y el usuario debe tener los permisos necesarios. Si se utiliza otra conexión, configurarla mediante `ConnectionStrings__MeetingTranscriber` antes de ejecutar la API y las herramientas de EF. La fábrica de migraciones lee archivos de configuración y variables de entorno; no lee User Secrets.

Con la API detenida:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update --project src/MeetingTranscriber.Infrastructure --startup-project src/MeetingTranscriber.Api --context MeetingTranscriberDbContext
```

La API **no aplica migraciones automáticamente**. Este comando aplica las migraciones pendientes, incluida `ErrorLoggingAndRetryLimits`, que añade el registro de errores y el contador de reintentos. El esquema utilizado es `transcriber`. La centralización de errores y la sustitución de los motores de IA no requieren migraciones adicionales.

### 7. Compilar y arrancar

```powershell
dotnet build MeetingTranscriber.slnx
dotnet run --project src/MeetingTranscriber.Api
```

El perfil incluido utiliza Development y publica:

- API HTTP: `http://localhost:52611`.
- API HTTPS: `https://localhost:52610`.
- Scalar: `http://localhost:52611/api-docs`.
- OpenAPI: `http://localhost:52611/openapi/v1.json`.

Para HTTPS local, confiar en el certificado de desarrollo con `dotnet dev-certs https --trust`. En Visual Studio, seleccionar `MeetingTranscriber.Api` como proyecto de inicio y ejecutar con F5.

## Comprobar la instalación

Los endpoints de negocio requieren `X-Api-Key`. Development incluye una clave conocida para pruebas; no es una credencial de OpenAI ni debe utilizarse en producción.

```powershell
$baseUrl = 'http://localhost:52611'
$headers = @{ 'X-Api-Key' = 'meeting-transcriber-local-development-key' }

Invoke-RestMethod "$baseUrl/health"
Invoke-RestMethod "$baseUrl/api/transcription/configuration" -Headers $headers
```

`ready` informa de los requisitos locales de transcripción; `summaryReady` consulta Ollama de manera independiente. Los campos `errorCode` y `summaryErrorCode` indican qué componente falta sin revelar rutas ni secretos. `/health` solo confirma que el proceso responde: no valida la base de datos ni los modelos.

Ejemplo de subida desde PowerShell, sustituyendo `reunion.mp4` por un archivo existente:

```powershell
$meeting = Invoke-RestMethod `
    -Uri "$baseUrl/api/meetings?fileName=reunion.mp4&language=es" `
    -Method Post -Headers $headers `
    -ContentType 'application/octet-stream' -InFile '.\reunion.mp4'

Invoke-RestMethod "$baseUrl/api/meetings/$($meeting.id)" -Headers $headers
```

La subida devuelve `202 Accepted` y una cabecera `Location`. Consultar el estado cada 2–5 segundos hasta `Completed` o `Failed`. Un `202` confirma la aceptación del trabajo, no que la transcripción ya haya terminado. El idioma predeterminado es `es`; se admiten códigos de dos letras.

## Procesamiento y almacenamiento

1. Se guarda una copia temporal del original y se encola el trabajo en SQL Server.
2. FFmpeg extrae la primera pista de audio y genera PCM mono de 16 kHz.
3. Community-1 analiza la reunión completa para mantener la identidad de los hablantes entre fragmentos.
4. Whisper transcribe ventanas de hasta diez minutos con contexto solapado de dos segundos a ambos lados.
5. Se atribuyen los segmentos a los turnos de voz y se guardan progreso y resultados. La atribución incierta queda como `speaker=null`.
6. Bajo petición, Ollama genera un resumen con referencias a segmentos. Las reuniones largas se procesan por partes; el resumen no se persiste en SQL.
7. El cliente descarga los archivos y puede confirmar su guardado para eliminar la copia temporal del servidor.

Los estados son `Queued`, `Processing`, `Completed` y `Failed`. El worker procesa reuniones secuencialmente; los trabajos interrumpidos requieren un reintento explícito. Las nuevas reuniones tienen una retención temporal de 24 horas por defecto. La limpieza elimina trabajos finalizados al vencer su plazo o al confirmar la exportación; su ejecución depende de que el worker esté disponible. Los registros de errores tienen una retención independiente.

El módulo [browser-local-storage.mjs](docs/browser-local-storage.mjs) permite integrar la selección de carpeta y exportación en el navegador. El backend no recibe la ruta local del usuario ni captura el micrófono. La integración visual y la grabación desde el frontal están pendientes.

## Endpoints

Todos los endpoints `/api` requieren `X-Api-Key`.

| Método | Ruta | Función |
|---|---|---|
| GET | `/health` | Comprobar que el proceso responde; público. |
| GET | `/api/transcription/configuration` | Consultar la disponibilidad de los motores. |
| POST | `/api/meetings?fileName=...&language=es` | Subir el archivo binario y obtener el ID del trabajo. |
| GET | `/api/meetings/{id}` | Consultar estado, progreso, reintentos y error. |
| GET | `/api/meetings/{id}/transcript` | Obtener segmentos y hablantes en JSON. |
| GET | `/api/meetings/{id}/transcript.txt` | Descargar la transcripción como TXT. |
| POST | `/api/meetings/{id}/summary` | Generar un borrador de resumen. |
| POST | `/api/meetings/{id}/retry` | Reencolar una reunión fallida dentro de su presupuesto. |
| GET | `/api/meetings/{id}/local-export` | Obtener el manifiesto de archivos para exportar. |
| GET | `/api/meetings/{id}/recording` | Descargar el original temporal. |
| POST | `/api/meetings/{id}/local-export/confirm` | Confirmar `{ "filesSaved": true }` y eliminar la copia temporal de una reunión finalizada. |
| GET | `/api/errors?meetingId=...&take=20` | Consultar errores; también admite `correlationId`. |

Formatos admitidos: `.mp3`, `.wav`, `.m4a`, `.flac`, `.ogg`, `.mp4`, `.mov`, `.mkv`, `.webm` y `.aac`. La subida utiliza `application/octet-stream`, sin JSON, base64 ni multipart. Transcripciones y resúmenes requieren estado `Completed`.

Consultar [docs/openapi.json](docs/openapi.json) para los DTO, estados HTTP y parámetros completos, y [docs/FRONTEND.md](docs/FRONTEND.md) para integrar el cliente.

## Configuración

La configuración compartida está en `src/MeetingTranscriber.Api/appsettings.json`; los archivos Development y Production contienen ajustes por entorno. Las variables de entorno utilizan `__` para separar niveles. Las rutas relativas se resuelven desde el proyecto API.

| Opción | Valor predeterminado / función |
|---|---|
| `Transcription:DataRoot` | `../../data`; trabajos temporales y medios. |
| `Transcription:WhisperModelPath` | Modelo `large-v3-turbo` descargado por el script. |
| `Transcription:WhisperThreads` | 8 hilos. |
| `Transcription:MaxUploadBytes` | 2.000.000.000 bytes. |
| `Transcription:MaxMeetingRetries` | 3 reintentos explícitos por reunión. |
| `Transcription:TemporaryRetentionHours` | 24 horas. |
| `Transcription:Diarization:Enabled` | `true`; CPU y entre 1 y 20 hablantes por defecto. |
| `Transcription:LocalSummary:BaseUrl` | `http://127.0.0.1:11434/`. |
| `Transcription:LocalSummary:Model` | `qwen3:8b`. |
| `Security:ApiKey` | Clave de acceso a este backend. |
| `Cors:AllowedOrigins` | Orígenes autorizados; Development admite los puertos 5173 y 3000 de localhost. |
| `AllowedHosts` | `localhost;127.0.0.1`; ajustar al publicar. |
| `RequestLimits` | Presupuestos, concurrencia y bloqueo de abuso. |

Usar variables de entorno o un almacén de secretos para credenciales propias. No guardar claves en Git. Los antiguos ajustes de proveedor de OpenAI no se utilizan. `appsettings.Local.json` está excluido de Git, pero **no se carga automáticamente** en la configuración actual.

## Errores y seguridad

`Error`, `FailureCode`, `ErrorCatalog` y `ErrorException` representan fallos de negocio y de motores. Una política central los traduce a HTTP; el generador de respuestas y el registro de diagnóstico se comparten entre controladores, autenticación, validación MVC y middleware.

- Los errores HTTP usan Problem Details con `code` y `correlationId`. Los errores de negocio ya existentes conservan `{ "error": "..." }` para compatibilidad con el frontal.
- Las respuestas controladas incluyen `X-Correlation-ID`. Los fallos del procesamiento asíncrono se consultan en el estado de la reunión y en `/api/errors`.
- Se registran códigos, origen y trazas de métodos; no mensajes brutos, contenido de reuniones, rutas privadas ni credenciales.
- Los errores de validación y rechazos por abuso o saturación no generan una fila por petición. Un fallo del logger no sustituye el error original.

Límites predeterminados:

| Control | Límite |
|---|---|
| Todas las IP | 600 peticiones por minuto. |
| Cada IP | 120 peticiones por minuto; ráfaga de 30 en diez segundos. |
| Lecturas por endpoint e IP | 60 por minuto. |
| Subidas / retries / resúmenes por endpoint e IP | 3 / 3 / 2 por minuto. |
| Peticiones simultáneas | 20 globales y 6 por IP. |
| Subidas simultáneas | 2 globales y 1 por IP. |
| Generación de resúmenes | Una operación de inferencia simultánea. |
| Seguimiento de clientes | Hasta 4096 IP. |

Los excesos de los límites HTTP devuelven `429` y `Retry-After`. Cinco fallos de autenticación, diez accesos a rutas inexistentes o tres infracciones de límites en un minuto activan un bloqueo `403`, de dos a quince minutos según reincidencia. La saturación del seguimiento devuelve `503 SecurityCapacityReached`; el motor de resumen ocupado devuelve `503 LocalInferenceBusy`. No se hacen reintentos automáticos de inferencia; el cliente debe respetar la espera y evitar repetir automáticamente operaciones POST.

La identidad IP procede de la conexión, sin confiar en cabeceras aportadas por el cliente. Los límites son por instancia. Publicar detrás de un proxy o túnel requiere configurar explícitamente los proxies de confianza y la IP real. La clave compartida está pensada para operadores: una aplicación pública con clientes independientes necesita autenticación individual y autorización por reunión. Los controles de la API no sustituyen la protección de red frente a ataques que saturen la conexión.

Production requiere HTTPS y una clave propia; también hay que configurar conexión SQL, rutas de motores, `AllowedHosts` y orígenes CORS. Los valores locales incluidos no constituyen una configuración de despliegue. Véase [docs/ERRORS-AND-LIMITS.md](docs/ERRORS-AND-LIMITS.md).

## Pruebas

La suite habitual usa almacenamiento y servidores HTTP aislados, sin modificar SQL ni descargar modelos:

```powershell
dotnet run --project tests/MeetingTranscriber.Checks -- --openapi docs/openapi.json
```

Comprueba reglas de negocio, reintentos atómicos, errores tipados, cuerpos HTTP, correlación, sanitización, fallos del logger, límites por IP y operación, bloqueos, cancelación, concurrencia, atribución de hablantes y contrato OpenAPI. Los motores se simulan salvo las comprobaciones locales que puedan ejecutarse con los recursos disponibles, como FFmpeg.

Comprobaciones adicionales:

```powershell
# Puente Python con pipeline simulado:
.\tools\diarization\.venv\Scripts\python.exe -m unittest discover -s tests/python

# Whisper nativo con el modelo descargado:
dotnet run --project tests/MeetingTranscriber.Checks -- --whisper

# Ollama activo y modelo local descargado:
dotnet run --project tests/MeetingTranscriber.Checks -- --ollama

# SQL Server con las migraciones aplicadas; crea y elimina filas de prueba:
dotnet run --project tests/MeetingTranscriber.Checks -- --sql

# Regenerar y validar el contrato:
dotnet run --project tests/MeetingTranscriber.Checks -- --export-openapi docs/openapi.json --openapi docs/openapi.json
```

Las opciones `--speech <archivo>` y `--speakers <archivo>` permiten comprobar muestras sintéticas controladas de voz y de dos hablantes en orden A-B-A. Las pruebas SQL deben ejecutarse contra una base de desarrollo o de pruebas. Ninguna de estas comprobaciones aplica migraciones automáticamente.

## Precisión y alcance

La diarización asigna etiquetas de voz; no descubre nombres reales ni separa físicamente conversaciones simultáneas. Las marcas de tiempo de Whisper son estimaciones. Ruido, acentos, interrupciones y voces solapadas pueden afectar a la transcripción y la atribución.

Los resúmenes son borradores y admiten hasta 500.000 caracteres de transcripción. Sus referencias se validan, pero no garantizan la exactitud semántica. Revisar nombres, fechas, cifras, decisiones y responsables antes de utilizar un acta definitiva. Las comprobaciones sintéticas no sustituyen una evaluación con reuniones reales.

El worker y el bloqueo de instancia están diseñados para una instalación local; no forman una cola distribuida. Los modelos de clientes, expedientes y participantes existen en SQL, pero sus endpoints CRUD todavía no están expuestos.

## Documentación

- [Instalación y modelos locales](docs/LOCAL-MODELS.md).
- [Procesamiento y transcripción](docs/TRANSCRIPTION.md).
- [Errores, diagnóstico y límites](docs/ERRORS-AND-LIMITS.md).
- [Integración del frontal](docs/FRONTEND.md).
- [Exportación local y retención](docs/LOCAL-STORAGE.md).
- [Contrato OpenAPI](docs/openapi.json).

Los modelos, el entorno Python, los medios temporales y los artefactos de compilación están excluidos de Git. Cada instalación debe preparar sus dependencias y revisar las licencias y condiciones de los motores y modelos que utilice.
