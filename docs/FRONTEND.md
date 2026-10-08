# Integración del frontal

Usa `openapi.json` para generar el cliente o pásaselo a Claude junto con este documento. El contrato es OpenAPI 3.0 y se genera desde los endpoints y DTO del backend.

## Conexión local

- API HTTP del perfil de Visual Studio: `http://localhost:52611`.
- API HTTPS: `https://localhost:52610` (requiere confiar en el certificado local).
- Interfaz interactiva en Development: **Scalar en `/api-docs`**, abierta automáticamente con F5 como en SyncForge. Muestra los endpoints y sus modelos, y permite ejecutar peticiones. Para probar `/api`, configurar la autenticación `ApiKey` con la clave local del backend.
- Contrato vivo: `/openapi/v1.json`. En Development se puede descargar sin clave; en otros entornos exige `X-Api-Key`.
- El contrato utiliza un servidor relativo `/`; configura la URL base anterior en el cliente generado.
- Endpoints `/api`: cabecera `X-Api-Key`. Para desarrollo, `meeting-transcriber-local-development-key`.
- `/health` no requiere clave y solo comprueba que el proceso responde.
- CORS de desarrollo admite `http://localhost:5173` y `http://localhost:3000`. Para otro puerto, añade el origen exacto a `Cors:AllowedOrigins` en `appsettings.Development.json`. No utilizar `mode: 'no-cors'`.
- Los modelos de transcripción, hablantes y resumen se ejecutan localmente en el backend; no hay clave de proveedor de IA. La clave pública de desarrollo del backend no debe convertirse en la autenticación de producción del navegador; esa etapa requiere autenticación individual.

## Flujo de pantallas

Antes de transcribir, consultar `GET /api/transcription/configuration` con `X-Api-Key`: informa del modelo, FFmpeg, diarización y resumen local, sin exponer rutas ni secretos. Si `ready=false`, mostrar el problema de configuración. Subida y reintento devuelven 503 con `ErrorResponse` si faltan el modelo, FFmpeg o los componentes de diarización. Véase [TRANSCRIPTION.md](TRANSCRIPTION.md) para los códigos de error y configuración del backend.

El estado de reunión añade `errorMessage` (texto controlado) y `retryCount`. Los códigos antiguos de cuota se conservan solo para trabajos históricos. Para nuevos fallos, mostrar el mensaje controlado y revisar modelos/runtime locales. HTTP 429 incluye `Retry-After`; respetar esos segundos también al hacer polling. No reintentar automáticamente POST de subida, resumen o retry, porque pueden duplicar trabajos o saturar los motores locales. Las respuestas incluyen `X-Correlation-ID`, expuesto por CORS, y los errores HTTP controlados pueden incluir `code` y `correlationId` en Problem Details. El endpoint `/api/errors` es para diagnóstico del operador. Ver [ERRORS-AND-LIMITS.md](ERRORS-AND-LIMITS.md).

HTTP 403 con `code=ClientTemporarilyBlocked` significa que la IP está bloqueada temporalmente. Detener llamadas y polling durante Retry-After; no intentar renovar la clave para saltar el bloqueo. HTTP 401 indica autenticación ausente o inválida: corregir la clave antes de repetir. Los límites normales son 60 lecturas/minuto por endpoint e IP, con 120 llamadas/minuto en total por IP y 30 por diez segundos incluyendo preflight. Una subida simultánea por IP; dos en total. Todas las opciones se documentan en ERRORS-AND-LIMITS.md.

1. Seleccionar un audio/vídeo y el idioma (por defecto `es`).
2. `SubmitMeeting`: enviar el archivo como cuerpo binario `application/octet-stream`, con `fileName` e `language` en la query. No enviar FormData, JSON ni base64. Límite por defecto: 2.000.000.000 bytes; formatos documentados en OpenAPI.
3. La respuesta `202` contiene `id` y estado. Su cabecera `Location` apunta a la consulta de progreso.
4. `GetMeeting`: consultar cada 2–5 segundos hasta `Completed` o `Failed`; detener el polling al llegar a uno de esos estados y al desmontar la pantalla. Durante `Queued` o preparación, `totalChunks` puede ser cero: mostrar progreso indeterminado. Con `totalChunks > 0`, calcular `completedChunks / totalChunks`.
5. `Completed`: usar `GetTranscript` para mostrar los segmentos o `DownloadTranscript` para descargar TXT.
6. `Failed`: mostrar `errorCode` y ofrecer `RetryMeeting`. El reintento borra segmentos anteriores y procesa toda la reunión; requiere una acción explícita del usuario porque volverá a consumir recursos locales.

## Ejemplo de subida desde el navegador

```typescript
const API_BASE = 'http://localhost:52611';
const API_KEY = 'meeting-transcriber-local-development-key'; // Solo desarrollo.

async function uploadMeeting(file: File, language = 'es') {
  const query = new URLSearchParams({ fileName: file.name, language });
  const response = await fetch(`${API_BASE}/api/meetings?${query}`, {
    method: 'POST',
    headers: {
      'X-Api-Key': API_KEY,
      'Content-Type': 'application/octet-stream',
    },
    body: file,
  });
  if (response.status !== 202) throw new Error(`Subida rechazada: ${response.status}`);
  return await response.json(); // MeetingStatusResponse.
}

async function getMeeting(id: string, signal?: AbortSignal) {
  const response = await fetch(`${API_BASE}/api/meetings/${id}`, {
    headers: { 'X-Api-Key': API_KEY }, signal,
  });
  if (!response.ok) throw new Error(`Consulta rechazada: ${response.status}`);
  return await response.json();
}

async function downloadTranscript(id: string) {
  const response = await fetch(`${API_BASE}/api/meetings/${id}/transcript.txt`, {
    headers: { 'X-Api-Key': API_KEY },
  });
  if (!response.ok) throw new Error(`Descarga rechazada: ${response.status}`);
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement('a');
  link.href = url;
  link.download = `${id}.txt`;
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
```

Para descargar no basta con un enlace directo: `fetch` permite enviar la cabecera de autenticación. CORS expone `Location` y `Content-Disposition` para que el frontal pueda leerlas.

## Respuestas y errores

Los nombres JSON son camelCase. `status` es un string: `Queued`, `Processing`, `Completed` o `Failed`. `errorCode` puede ser null; `createdAt` es ISO 8601. No interpretar el porcentaje como avance en tiempo real del reconocimiento de voz.

`TranscriptResponse` contiene `timing: 'segment'`, `speakerScope: 'meeting'` y `segments`. Los tiempos `startSeconds` y `endSeconds` son números en segundos desde el inicio. `speaker` puede ser null. Las etiquetas locales Hablante 1 y Hablante 2 se conservan entre fragmentos de la misma reunión. Los resultados antiguos con etiquetas chunk-* mantienen speakerScope=chunk. Los nombres reales requieren revisión.

| Código | Tratamiento |
|---|---|
| `400` | Validación tipada, incluida la de binding MVC: Problem Details con `code` y `correlationId`. La confirmación sin archivos guardados conserva `{ "error": "LocalFilesNotSaved" }`. |
| `401` | Clave ausente/incorrecta: `AuthenticationRequired` en Problem Details. |
| `403` | Acceso denegado o bloqueo temporal. Respetar `Retry-After` cuando exista. |
| `404` | `MeetingNotFound`, `RecordingNotFound` o `ResourceNotFound` en Problem Details. |
| `409` | Conflicto de negocio con `{ "error": "..." }`. JSON, TXT y resumen comparten `TranscriptNotCompleted`; reintentos y confirmación tienen sus códigos específicos. |
| `413` | Archivo, cuerpo o transcripción de resumen demasiado grande: Problem Details. |
| `415` | Content-Type incorrecto: `UnsupportedMediaType` en Problem Details. |
| `429` | Límite alcanzado. Respetar `Retry-After`. |
| `500` | Fallo interno: código seguro y correlación, sin mensajes ni trazas privadas. |
| `502/503/504` | Fallo, indisponibilidad o timeout del motor. Los requisitos de transcripción mantienen `ErrorResponse`; los demás usan Problem Details. |

Los errores controlados incluyen `X-Correlation-ID`. Para obtener el código, leer `body.error` en ErrorResponse o `body.code` en Problem Details. Comprobar Content-Type y longitud antes de llamar a `response.json()`, porque un rechazo del transporte puede ocurrir antes del middleware. Un `202` no garantiza que el procesamiento posterior termine correctamente: hay que consultar el estado.

El frontal solo puede gestionar los endpoints presentes en el contrato. Clientes, expedientes y participantes tienen modelos SQL, pero aún no tienen endpoints CRUD. Tampoco hay listados, cancelación ni edición de transcripciones. La exportación local, el borrado de la copia temporal y el resumen por IA se describen en [LOCAL-STORAGE.md](LOCAL-STORAGE.md); incorporar [browser-local-storage.mjs](browser-local-storage.mjs) al flujo de selección de carpeta.

## Actualizar el archivo de contrato

Con la API arrancada en Development:

```powershell
curl.exe 'http://localhost:52611/openapi/v1.json' -o docs/openapi.json
```

La URL viva refleja los cambios de rutas y DTO; actualizar este archivo al entregar cambios al frontal. Si se arranca con otra URL, sustituir el host/puerto tanto al descargar como en el cliente.


