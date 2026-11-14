import http from 'node:http';
import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';

// Isolated protocol peer for real browser -> Rust HTTP -> S3 integration.
// No external credentials or cloud resources are used.
export async function withS3Fixture(run) {
  const state = { accessKey: 'fixture-access', objects: new Map(), gets: 0, puts: 0 };
  const server = http.createServer(async (request, response) => {
    try {
      const chunks = []; let size = 0;
      for await (const chunk of request) { size += chunk.length; assert(size <= 10 * 1024 * 1024); chunks.push(chunk); }
      const body = Buffer.concat(chunks);
      if (!request.headers.authorization?.startsWith(`AWS4-HMAC-SHA256 Credential=${state.accessKey}/`)) {
        response.writeHead(403); response.end('<Error><Code>ExpiredToken</Code></Error>'); return;
      }
      assert.equal(request.headers['x-amz-content-sha256'], createHash('sha256').update(body).digest('hex'));
      const key = new URL(request.url, 'http://localhost').pathname;
      if (request.method === 'PUT') { state.puts++; state.objects.set(key, body); response.writeHead(200, { 'Content-Length': 0 }); response.end(); }
      else if (request.method === 'GET') {
        state.gets++; const bytes = state.objects.get(key);
        if (!bytes) { response.writeHead(404); response.end(); return; }
        response.writeHead(200, { 'Content-Length': bytes.length }); response.end(bytes);
      } else if (request.method === 'DELETE') { state.objects.delete(key); response.writeHead(204); response.end(); }
      else { response.writeHead(400); response.end('<Error><Code>UnexpectedOperation</Code></Error>'); }
    } catch { response.writeHead(500); response.end('<Error><Code>FixtureFailure</Code></Error>'); }
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  state.endpoint = `http://127.0.0.1:${server.address().port}`;
  try { await run(state); } finally { server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); }
}
