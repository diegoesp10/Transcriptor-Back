import assert from 'node:assert/strict';
import { saveMeetingLocally, confirmLocalExport } from '../docs/browser-local-storage.mjs';

const files = new Map();
const calls = [];
const directory = {
  queryPermission: async () => 'granted',
  getDirectoryHandle: async () => directory,
  getFileHandle: async name => ({ createWritable: async () => {
    const chunks = [];
    return new WritableStream({
      write(chunk) { chunks.push(Buffer.from(chunk)); },
      close() { files.set(name, Buffer.concat(chunks).toString()); },
    });
  } }),
};
const summary = { summary: { overview: 'Resumen', highlights: [{ text: 'Punto', evidenceSegmentIndices: [0] }],
  decisions: [], actionItems: [], openQuestions: [] } };
globalThis.fetch = async (url, options) => {
  calls.push({ url: String(url), method: options.method });
  if (String(url).endsWith('/confirm')) return new Response(null, { status: 204 });
  if (String(url).endsWith('/summary')) return Response.json(summary);
  return new Response('Contenido de prueba');
};
const manifest = { meetingId: 'test', folderName: 'meeting-test', canConfirmExport: true, artifacts: [
  { kind: 'recording', fileName: 'recording.webm', url: '/api/meetings/test/recording', method: 'GET', available: true },
  { kind: 'transcript-text', fileName: 'transcript.txt', url: '/api/meetings/test/transcript.txt', method: 'GET', available: true },
  { kind: 'summary-json', fileName: 'summary.json', url: '/api/meetings/test/summary', method: 'POST', available: true },
] };
const parameters = { directory, manifest, apiBase: 'https://localhost:52610', apiKey: 'test-key' };
const receipt = await saveMeetingLocally(parameters);
assert.equal(files.size, 4);
assert.equal(calls.some(c => c.url.endsWith('/confirm')), false, 'Saving must not delete server data automatically');
assert.equal(JSON.parse(files.get('summary.json')).summary.overview, 'Resumen');
assert.match(files.get('summary.txt'), /segmentos: 0/);
await confirmLocalExport({ ...parameters, receipt });
assert.equal(calls.at(-1).method, 'POST');
await assert.rejects(() => saveMeetingLocally({ ...parameters, directory: { queryPermission: async () => 'denied' } }), /FolderPermissionRequired/);
await assert.rejects(() => saveMeetingLocally({ ...parameters, manifest: { ...manifest, folderName: '../escape' } }), /InvalidFileName/);
await assert.rejects(() => saveMeetingLocally({ ...parameters, manifest: { ...manifest, artifacts: [
  ...manifest.artifacts.slice(0, 2), { ...manifest.artifacts[2], url: 'https://external.example/api/meetings/test/summary' },
] } }), /InvalidArtifactUrl/);
console.log('Browser storage checks passed: streaming, summary exports, permissions, paths, origin and explicit deletion.');
