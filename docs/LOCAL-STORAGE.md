# Destino local y procesamiento temporal

La elección acordada es **carpeta local como destino final**. El navegador recibe autorización de escritura; el servidor no recibe la ruta ni el handle. El login de la herramienta y el permiso del sistema de archivos son independientes. Una carpeta elegida en un ordenador no se puede utilizar automáticamente desde otro dispositivo.

## Flujo que debe implementar el frontal

1. Antes de grabar/subir, preguntar **¿Dónde quieres guardar las reuniones?** y ofrecer **Seleccionar carpeta**. Desde ese clic, ejecutar `chooseLocalFolder()` de `browser-local-storage.mjs`. Para grabaciones nuevas, pedir por separado el permiso de micrófono. El backend actual sigue recibiendo archivos terminados; no captura el micrófono.
2. Mantener el `FileSystemDirectoryHandle` en el navegador. Se puede recordar en IndexedDB por usuario con `rememberFolder`; al restaurarlo, comprobar permisos. Si faltan, mostrar un botón para renovarlos. No guardar una supuesta ruta `C:\...` en SQL ni asumir que el permiso es permanente.
3. Subir el archivo como hasta ahora. Las nuevas reuniones reciben `temporaryExpiresAt` (24 horas desde la subida por defecto). Mostrar que esa es una copia temporal de procesamiento. Si el plazo vence mientras procesa, se elimina después de finalizar, cuando el worker puede hacerlo.
4. Consultar `GET /api/meetings/{id}/local-export`. Devuelve el nombre de subcarpeta `meeting-{id}`, la caducidad y los archivos disponibles. No ofrece carpeta local en el servidor.
5. Cuando finalice, llamar a `saveMeetingLocally`. Se guardan original, transcripción JSON/TXT y, si se solicita y hay proveedor configurado, resumen JSON/TXT. Los archivos grandes se copian por streaming. Si hay error de disco, permiso o red, conservar la copia temporal y mostrar el fallo.
6. Solo después de terminar las escrituras, ofrecer **Confirmar guardado y eliminar copia temporal** y llamar a `confirmLocalExport`. Este endpoint elimina grabación y datos de transcripción SQL; desaparecen las consultas posteriores. El servidor confía en la confirmación del cliente: no puede comprobar físicamente la carpeta del usuario.

El módulo está preparado para incorporarlo al frontal de Claude; no se ha integrado todavía en su interfaz. No iniciar `showDirectoryPicker()` al cargar la página ni después de esperar una llamada HTTP: necesita interacción directa del usuario.

## Compatibilidad y alternativa

`showDirectoryPicker` exige contexto seguro y no está disponible en todos los navegadores. Detectar la función; en los que no la soportan, ofrecer descargas explícitas. En ese modo el navegador decide cómo elegir el destino: no prometer acceso continuo a una carpeta. No confundir OPFS/cache del navegador con una carpeta visible de documentos.

Para reuniones largas grabadas desde el frontal, escribir los chunks de `MediaRecorder` progresivamente en la carpeta seleccionada, gestionar cuota/errores y no mantener toda la grabación en memoria. El módulo de esta entrega exporta archivos ya terminados; la captura y escritura incremental deberán incorporarse al frontal.

Fuentes: [selección de carpetas y restricciones](https://developer.mozilla.org/en-US/docs/Web/API/Window/showDirectoryPicker), [handles, permisos e IndexedDB](https://developer.chrome.com/docs/capabilities/web-apis/file-system-access).

## Contratos nuevos

| Método y ruta | Función |
|---|---|
| `GET /api/meetings/{id}/local-export` | Manifiesto de archivos y vencimiento de la copia temporal. |
| `GET /api/meetings/{id}/recording` | Original por streaming para guardarlo localmente. |
| `POST /api/meetings/{id}/summary` | Genera y devuelve un resumen; no guarda una copia en SQL. |
| `POST /api/meetings/{id}/local-export/confirm` | Recibe `{ "filesSaved": true }`; elimina datos y medios de un trabajo finalizado; devuelve 204. |

Todos requieren `X-Api-Key`. No se modifica la subida binaria ni se añaden rutas locales a sus parámetros.

El resumen contiene `overview`, `highlights`, `decisions`, `actionItems` y `openQuestions`. Cada punto incluye `text` y `evidenceSegmentIndices`, índices base cero del array original de segmentos. Se marcan como borradores que requieren revisión humana; una referencia válida no certifica la verdad del contenido. No se infieren identidades ni compromisos que no consten en la conversación.

La generación usa IMeetingSummaryProvider y Ollama local, con Transcription:LocalSummary:Model (qwen3:8b por defecto). No utiliza clave ni API de IA externa. Procesa hasta 500.000 caracteres por partes y consolida el resultado; timeout configurable, por defecto 20 minutos. Devuelve 503 si Ollama/modelo no están disponibles, 413 si supera el límite y 502 ante JSON/referencias incompatibles. La disponibilidad del resumen se consulta en GET /api/transcription/configuration. Ver LOCAL-MODELS.md.

## Retención y alcance del borrado

La migración `TemporaryLocalStorage` añade la caducidad nullable. Los registros anteriores no se borran automáticamente: carecen de esa caducidad. Los nuevos finalizados se borran al confirmar exportación o al vencer el plazo. Los trabajos activos no se eliminan mientras procesan. Un fallo/pausa del worker retrasa la limpieza; archivos huérfanos sin fila SQL se limpian después de 24 horas cuando vuelve a funcionar.

Este borrado alcanza la BD activa y el directorio de medios del backend. No borra copias de proveedores, backups ni snapshots; esas políticas se deben configurar por separado antes de operar con clientes reales. La API actual todavía utiliza una clave compartida: se necesitan usuarios y autorización por propietario para desplegar esta herramienta online.

