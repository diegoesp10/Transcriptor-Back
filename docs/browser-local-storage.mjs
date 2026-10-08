// Import into the frontend. Handles remain in the browser; never send paths or handles to the API.
export const supportsLocalFolder = () => typeof window !== 'undefined'
  && window.isSecureContext && typeof window.showDirectoryPicker === 'function';

export async function chooseLocalFolder() {
  if (!supportsLocalFolder()) throw new Error('LocalFolderUnavailable');
  // Call directly from a button click, before awaiting any HTTP request.
  return window.showDirectoryPicker({ id: 'meeting-artifacts', mode: 'readwrite', startIn: 'documents' });
}

function safeName(name) {
  if (!name || name === '.' || name === '..' || /[\\/:\u0000-\u001f]/.test(name)) throw new Error('InvalidFileName');
  return name;
}

async function requirePermission(directory) {
  if (await directory.queryPermission({ mode: 'readwrite' }) !== 'granted')
    throw new Error('FolderPermissionRequired');
}

export async function renewFolderPermission(directory) {
  // A user action is required when asking for permission again.
  return await directory.requestPermission({ mode: 'readwrite' }) === 'granted';
}

async function writeStream(directory, name, stream) {
  const file = await directory.getFileHandle(safeName(name), { create: true });
  const writable = await file.createWritable();
  // pipeTo closes the file only on successful completion and aborts on failure.
  await stream.pipeTo(writable);
  return file;
}

function apiUrl(base, path) {
  const baseUrl = new URL(base);
  const target = new URL(path, baseUrl);
  if (target.origin !== baseUrl.origin || !target.pathname.startsWith('/api/meetings/'))
    throw new Error('InvalidArtifactUrl');
  return target;
}

function summaryText(response) {
  const summary = response.summary;
  const categories = [['Puntos destacados', summary.highlights], ['Decisiones', summary.decisions],
    ['Tareas', summary.actionItems], ['Preguntas pendientes', summary.openQuestions]];
  return ['Borrador generado por IA. Requiere revisión humana.', summary.overview,
    ...categories.flatMap(([title, points]) => [title, ...points.map(p =>
      `- ${p.text} [segmentos: ${p.evidenceSegmentIndices.join(', ')}]`)])].join('\n\n');
}

export async function saveMeetingLocally({ directory, manifest, apiBase, apiKey, includeSummary = true, signal }) {
  await requirePermission(directory);
  if (!manifest.canConfirmExport) throw new Error('MeetingStillActive');
  if (includeSummary && !manifest.artifacts.some(a => a.kind === 'summary-json' && a.available))
    throw new Error('SummaryUnavailable');
  const folder = await directory.getDirectoryHandle(safeName(manifest.folderName), { create: true });
  const files = [];
  for (const artifact of manifest.artifacts) {
    if (!artifact.available || (artifact.kind === 'summary-json' && !includeSummary)) continue;
    const response = await fetch(apiUrl(apiBase, artifact.url), {
      method: artifact.method, headers: { 'X-Api-Key': apiKey }, signal,
    });
    if (!response.ok) throw new Error(`ArtifactFailed:${artifact.kind}:${response.status}`);
    if (artifact.kind === 'summary-json') {
      const summary = await response.json();
      await writeStream(folder, artifact.fileName, new Blob([JSON.stringify(summary, null, 2)], { type: 'application/json' }).stream());
      await writeStream(folder, 'summary.txt', new Blob([summaryText(summary)], { type: 'text/plain' }).stream());
      files.push(artifact.fileName, 'summary.txt');
    } else {
      if (!response.body) throw new Error('EmptyArtifactResponse');
      await writeStream(folder, artifact.fileName, response.body);
      files.push(artifact.fileName);
    }
  }
  // No server deletion here: expose a separate user action after all files are closed.
  return { meetingId: manifest.meetingId, folderName: manifest.folderName, files, filesSaved: true };
}

export async function confirmLocalExport({ apiBase, apiKey, receipt, signal }) {
  if (!receipt.filesSaved || !receipt.files.length) throw new Error('LocalFilesNotSaved');
  const response = await fetch(apiUrl(apiBase, `/api/meetings/${receipt.meetingId}/local-export/confirm`), {
    method: 'POST', headers: { 'X-Api-Key': apiKey, 'Content-Type': 'application/json' },
    body: JSON.stringify({ filesSaved: true }), signal,
  });
  if (response.status !== 204) throw new Error(`ExportConfirmationFailed:${response.status}`);
}

function openPreferences() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open('meeting-local-preferences', 1);
    request.onupgradeneeded = () => request.result.createObjectStore('folders');
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

export async function rememberFolder(directory, userScope) {
  // Use a separate key per authenticated user and clear UI state when switching accounts.
  const db = await openPreferences();
  try {
    await new Promise((resolve, reject) => {
      const transaction = db.transaction('folders', 'readwrite');
      transaction.objectStore('folders').put(directory, userScope);
      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error);
      transaction.onabort = () => reject(transaction.error);
    });
  } finally { db.close(); }
}

export async function restoreFolder(userScope) {
  const db = await openPreferences();
  try {
    return await new Promise((resolve, reject) => {
      const request = db.transaction('folders').objectStore('folders').get(userScope);
      request.onsuccess = () => resolve(request.result ?? null);
      request.onerror = () => reject(request.error);
    });
  } finally { db.close(); }
}
