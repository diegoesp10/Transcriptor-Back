# Errores, límites y diagnóstico

## Causa de la transcripción fallida

Los trabajos históricos fallaron por cuota del proveedor externo. Ese adaptador se ha sustituido por Whisper.net, Community-1 y Ollama locales. No hay que añadir saldo ni configurar una clave de IA. Los códigos históricos se conservan en BD; los nuevos requisitos y errores están documentados en TRANSCRIPTION.md y LOCAL-MODELS.md.

## Aplicar la migración pendiente

Se ha preparado **ErrorLoggingAndRetryLimits**, sin aplicarla. Detener la API en Visual Studio y ejecutar desde la raíz:

```powershell
dotnet ef database update --project src/MeetingTranscriber.Infrastructure --startup-project src/MeetingTranscriber.Api --context MeetingTranscriberDbContext
```

La migración añade `transcriber.ErrorLogs` y `Meetings.RetryCount` (cero para filas existentes). Los límites nuevos requieren aplicar esta migración antes de arrancar la API.

## Arquitectura del tratamiento de errores

`Application/Error.cs` define `Error`, su categoría (`ErrorKind`) y `ErrorException`. `FailureCode` contiene los códigos tipados; `ErrorCatalog` centraliza mensajes seguros, origen y política de registro. Los valores existentes del enum mantienen su orden para conservar los registros históricos. Las reglas de negocio se ejecutan en los servicios: reunión inexistente, transcripción pendiente, presupuesto de reintentos y confirmación de exportación. `Api/Services/MeetingRequests` coordina los requisitos de subida y disponibilidad de motores. Los controladores solo invocan servicios y construyen respuestas de éxito.

`Api/Errors/ApiErrorPolicy` traduce categorías a estados HTTP. `ApiErrorResponder` construye las respuestas y conserva CORS, correlación y Retry-After. También se utiliza desde autenticación, límites, validación MVC y respuestas vacías del framework (por ejemplo 404, 405 y 415). `ApiErrorClassifier` traduce excepciones desconocidas sin publicar su mensaje; `RequestExceptionMiddleware` únicamente coordina el flujo. `ApiErrorRecorder` registra los fallos operativos, conserva el origen real de Whisper/Ollama y protege la respuesta si falla el logger.

Los códigos de negocio que el frontal ya consume conservan el formato `{ "error": "TranscriptNotCompleted" }`. Los demás errores usan `application/problem+json` con `code` y `correlationId`. Todas las respuestas controladas llevan `X-Correlation-ID`. Las operaciones de transcripción JSON, TXT y resumen comparten la misma regla de disponibilidad; sus conflictos ahora devuelven un cuerpo de error controlado. Los errores de validación y los rechazos por abuso, incluida la saturación `503 SecurityCapacityReached`, no insertan filas en BD. La cancelación de un cliente tampoco se registra como fallo operativo.

Para añadir un fallo, se añade un valor al final de `FailureCode` y su descripción/política al catálogo. El servicio lanza `ErrorException` con ese código, o una excepción especializada que derive de ella. No se añaden mensajes, códigos literales ni traducciones HTTP en los controladores. Esta refactorización no cambia el esquema SQL ni requiere una migración nueva.

## Datos registrados

El registro guarda código enumerado, mensaje del catálogo, fecha, origen, reunión/fragmento, intentos, estado HTTP, código y request ID del proveedor, correlación, tipo de excepción y traza de métodos. Excluye mensajes brutos, rutas, nombres de archivos, audio, transcripciones, claves y cabeceras de autenticación. Permanece aunque se elimine la reunión temporal; su retención se gestiona por separado. Si falla la escritura SQL, se conserva el error original en el log estructurado de consola.

`GET /api/errors?meetingId=<uuid>&take=20` requiere `X-Api-Key`. Admite `correlationId`; `take` entre 1 y 100. Es un endpoint para operadores con acceso al backend; no incluirlo en una interfaz pública. Las respuestas HTTP incluyen `X-Correlation-ID`; los errores no controlados usan Problem Details con `code` y `correlationId`. Los fallos asíncronos se consultan por `meetingId`. El estado de reunión añade `errorMessage` y `retryCount`.

## Límites configurables

La admisión y los límites se aplican después de identificar la ruta y antes de CORS, autenticación y lectura del cuerpo. Incluyen las llamadas con claves inválidas y los preflight OPTIONS. Cada IP tiene su propio presupuesto por endpoint, aunque los clientes utilicen la misma clave. Variar UUID, query strings, claves o cabeceras de IP no reinicia los contadores. IPv4 y su representación IPv6 mapeada comparten identidad. HEAD comparte el presupuesto de GET cuando la ruta lo admite.

Se utiliza únicamente la IP de conexión. No se confía en X-Forwarded-For, X-Real-IP ni otras cabeceras del cliente. Cuando se publique detrás de un túnel/proxy habrá que configurar explícitamente la IP real y los proxies de confianza; de lo contrario, sus clientes pueden compartir la IP del proxy. No habilitar confianza indiscriminada en esas cabeceras.

Valores predeterminados; se pueden añadir a `appsettings.json` o configurar por entorno:

```json
{
  "RequestLimits": {
    "GlobalPerMinute": 600,
    "PerIpPerMinute": 120,
    "ReadPerMinute": 60,
    "WritePerMinute": 10,
    "UploadPerMinute": 3,
    "RetryPerMinute": 3,
    "SummaryPerMinute": 2,
    "MaxConcurrentRequests": 20,
    "PerIpConcurrentRequests": 6,
    "MaxConcurrentUploads": 2,
    "PerIpBurstRequests": 30,
    "BurstWindowSeconds": 10,
    "AuthenticationFailuresPerMinute": 5,
    "UnknownRoutesPerMinute": 10,
    "RateViolationsPerMinute": 3,
    "BlockSeconds": 120,
    "MaxBlockSeconds": 900,
    "MaxTrackedIps": 4096,
    "IdleRetentionMinutes": 30,
    "MaxNonUploadBodyBytes": 16384,
    "EndpointPerMinute": {}
  },
  "Transcription": {
    "MaxMeetingRetries": 3
  }
}
```

Los presupuestos por IP y endpoint usan ventanas deslizantes de un minuto, divididas en seis segmentos. El presupuesto global utiliza una ventana móvil de un minuto y la ráfaga una ventana móvil de diez segundos. No hay cola HTTP. El exceso devuelve **429** y `Retry-After`; si el limitador no ofrece una estimación de espera, se indican 60 segundos. El frontal debe respetarlo y no reintentar automáticamente POST de subida, resumen o retry. Se permite una subida simultánea por IP y dos en total; consultar el progreso sigue siendo posible durante una subida.

Para ajustar una operación concreta se puede añadir, por ejemplo, `"GET /api/meetings/{id}": 90` dentro de `EndpointPerMinute`. Utilizar la plantilla de OpenAPI, sin UUID concreto ni restricciones como `:guid`. Ese límite solo reemplaza el de la operación: se mantienen los límites global, por IP, de ráfaga y concurrencia.

## Bloqueo de abuso y protección de recursos

Cinco respuestas 401 en un minuto, diez accesos a rutas inexistentes o tres excesos de límites por IP activan un bloqueo temporal. Una respuesta 404 de una operación válida (por ejemplo una reunión que no existe) no se considera exploración de rutas. Un error aislado no basta para clasificar un ataque. La primera penalización dura dos minutos; la reincidencia duplica el tiempo hasta un máximo de quince minutos. Durante el bloqueo se devuelve **403**, `code=ClientTemporarilyBlocked`, `correlationId` y `Retry-After`, antes de ejecutar autenticación o controladores. Las peticiones durante un bloqueo no prolongan su duración. La saturación global y la concurrencia ocupada no generan penalizaciones de abuso.

La memoria de seguimiento tiene un máximo de 4096 IP; las entradas inactivas se eliminan después de 30 minutos, sin eliminar bloqueos activos ni peticiones en curso. Si no queda capacidad se devuelve **503 SecurityCapacityReached**, sin crear más estado. Los rechazos masivos no insertan filas individuales en BD ni conservan claves, cuerpos o cabeceras. Los contadores y bloqueos son por instancia y se reinician con el proceso; varias instancias requieren límites compartidos en el proxy/gateway.

Los cuerpos de operaciones que no son subida se limitan a 16 KiB, incluso si se envían por chunks sin Content-Length; el exceso devuelve **413 RequestBodyTooLarge**. Las subidas conservan el límite de 2 GB y la validación por streaming. TRACE y CONNECT se rechazan con **405 HttpMethodNotAllowed**. Kestrel limita conexiones simultáneas, cantidad/tamaño de cabeceras, tiempo de recepción de cabeceras e ingreso demasiado lento. Los rechazos del transporte pueden producirse antes del middleware.

Las respuestas añaden nosniff, no-referrer y DENY para marcos; los endpoints /api usan Cache-Control: no-store. Los rechazos anticipados respetan la política CORS configurada para que un navegador autorizado pueda leer el error. Las claves duplicadas o de más de 256 caracteres se rechazan sin procesarlas. Producción exige HTTPS y una clave propia: la configuración de producción ya no contiene la clave conocida de desarrollo y no admite utilizarla por accidente. Configurar Security:ApiKey mediante un secreto o variable de entorno al publicar. Development sigue permitiendo las pruebas locales habituales.

Estos controles reducen abuso y consumo de recursos dentro de la aplicación. Un ataque que sature la conexión de Internet necesita protección de red antes de llegar al PC. La clave compartida sigue siendo autenticación para operadores; una aplicación comercial con clientes independientes necesitará usuarios y autorización por reunión. Esta tarea no publica la API ni aplica migraciones.

La inferencia local no realiza reintentos automáticos. Whisper y la diarización usan timeouts y cancelación. El resumidor admite una operación simultánea; devuelve LocalInferenceBusy (503) si ya está ocupado.

`MaxMeetingRetries` (0–10) limita reintentos explícitos durante la vida de la reunión. El contador se incrementa atómicamente con Failed → Queued y persiste tras reiniciar. El presupuesto agotado devuelve **409** con `MeetingRetryLimitExceeded`. Un retry reencola la reunión completa y vuelve a consumir recursos del equipo.

## Verificación

`dotnet run --project tests/MeetingTranscriber.Checks` comprueba reintentos, sanitización, fallos del logger, atribución global de hablantes, resúmenes estructurados, bloqueo de proveedores remotos y límites HTTP sin cambios SQL. Las pruebas de seguridad usan Kestrel aislado: verifican presupuestos independientes por IP, fallos repetidos de autenticación, expiración/escalado del bloqueo, cabeceras falsificadas, rutas variables, preflight, límites de memoria/cuerpo/concurrencia, liberación de slots, CORS en rechazos y credenciales de producción. Después de aplicar la migración, `dotnet run --project tests/MeetingTranscriber.Checks -- --sql` comprueba además persistencia de errores y contadores con filas de prueba que se eliminan al terminar.

Referencias de diseño: [limitadores de ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0), [seguridad REST de OWASP](https://cheatsheetseries.owasp.org/cheatsheets/REST_Security_Cheat_Sheet.html).

Las opciones --whisper y --ollama ejecutan comprobaciones reales locales con contenido sintético. La descarga de Community-1 y su validación offline se realizan con scripts/Install-LocalDiarization.ps1 -DownloadModel.

