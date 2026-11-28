import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createHash, randomUUID } from 'node:crypto';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { spawnProcessTree, stopProcessTree } from './lib/child-process-tree.mjs';

assert.equal(process.platform, 'linux', 'Run the manual Linux QEMU acceptance workflow');
const repo = path.resolve(import.meta.dirname, '..'), execute = promisify(execFile);
const root = path.join(repo, 'artifacts/s3-cpu-compatibility'); fs.mkdirSync(root, { recursive: true });
const secret = 'Isolated-CPU-Compatibility-2026', accessKey = 'isolated-cpu', bucket = 'cpu-' + randomUUID();
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const results = [];
for (const provider of ['seaweedfs', 'rustfs']) {
  const output = path.join(root, provider); fs.mkdirSync(output, { recursive: true });
  const data = path.join(output, 'data'); fs.mkdirSync(data, { recursive: true });
  const binary = path.join(repo, '.codex-runtime/storage-cpu', provider, provider === 'seaweedfs' ? 'weed' : 'rustfs');
  const version = await execute('qemu-x86_64', ['-cpu', 'Conroe', binary, provider === 'seaweedfs' ? 'version' : '--version'], { timeout: 60000 });
  const args = provider === 'seaweedfs' ? ['mini', '-dir=' + data, '-ip=127.0.0.1', '-ip.bind=127.0.0.1', '-s3.port=18333', '-admin.ui=false', '-webdav=false', '-master.telemetry=false', '-volume.max=4', '-master.volumeSizeLimitMB=64'] : ['server', '--address', '127.0.0.1:18333', '--console-address', '127.0.0.1:18334', data];
  const log = fs.openSync(path.join(output, 'server.log'), 'a');
  const server = spawnProcessTree('qemu-x86_64', ['-cpu', 'Conroe', binary, ...args], { cwd: output, stdio: ['ignore', log, log], env: { ...process.env,
    AWS_ACCESS_KEY_ID: accessKey, AWS_SECRET_ACCESS_KEY: secret, WEED_ADMIN_PASSWORD: secret,
    RUSTFS_ACCESS_KEY: accessKey, RUSTFS_SECRET_KEY: secret, RUSTFS_REGION: 'us-east-1', RUSTFS_CONSOLE_ENABLE: 'false', RUSTFS_OBS_LOG_DIRECTORY: path.join(output, 'logs'), RUSTFS_TARGET_STORE_DIR: path.join(output, 'queues') } });
  try {
    const origin = 'http://127.0.0.1:18333', deadline = Date.now() + 180000; let ready = false, requestNumber = 0;
    while (Date.now() < deadline) {
      assert(server.exitCode === null, provider + ' exited under the Conroe instruction set');
      try { await fetch(origin, { signal: AbortSignal.timeout(1000) }); ready = true; break; } catch {}
      await new Promise(resolve => setTimeout(resolve, 250));
    }
    assert(ready, provider + ' startup timed out');
    const request = async (method, key = '', input, extra = []) => {
      const prefix = path.join(output, 'request-' + (++requestNumber));
      const args = ['--silent', '--show-error', '--max-time', '120', '--aws-sigv4', 'aws:amz:us-east-1:s3', '--user', accessKey + ':' + secret,
        '--request', method, '--output', prefix + '.body', '--dump-header', prefix + '.headers', '--write-out', '%{http_code}', ...extra];
      if (input) { fs.writeFileSync(prefix + '.input', input); args.push('--data-binary', '@' + prefix + '.input'); }
      args.push(origin + '/' + bucket + key);
      const { stdout } = await execute('curl', args, { timeout: 130000 }).catch(() => { throw Error(provider + ' signed S3 request failed'); });
      return { status: Number(stdout), body: fs.readFileSync(prefix + '.body'), headers: fs.readFileSync(prefix + '.headers', 'utf8') };
    };
    let created;
    do {
      created = await request('PUT');
      if (created.status !== 503) break;
      await new Promise(resolve => setTimeout(resolve, 500));
    } while (Date.now() < deadline);
    assert.equal(created.status, 200, created.body.toString());
    const race = await Promise.all(Array.from({ length: 12 }, (_, i) => request('PUT', '/conditional', Buffer.from(String(i)), ['--header', 'If-None-Match: *'])));
    assert.equal(race.filter(r => r.status === 200).length, 1); assert.equal(race.filter(r => r.status === 412).length, 11);
    const begin = await request('POST', '/large?uploads'); assert.equal(begin.status, 200);
    const upload = begin.body.toString().match(/<UploadId>([^<]+)<\/UploadId>/u)?.[1]; assert(upload);
    const chunks = [Buffer.alloc(6 * 1024 * 1024, 73), Buffer.alloc(6 * 1024 * 1024, 86)], parts = [];
    for (const [index, chunk] of chunks.entries()) {
      const r = await request('PUT', '/large?partNumber=' + (index + 1) + '&uploadId=' + encodeURIComponent(upload), chunk); assert.equal(r.status, 200);
      const tag = r.headers.match(/^etag:\s*([^\r\n]+)/imu)?.[1]; assert(tag);
      parts.push('<Part><PartNumber>' + (index + 1) + '</PartNumber><ETag>' + tag.trim() + '</ETag></Part>');
    }
    const complete = await request('POST', '/large?uploadId=' + encodeURIComponent(upload), Buffer.from('<CompleteMultipartUpload>' + parts.join('') + '</CompleteMultipartUpload>'));
    assert.equal(complete.status, 200); assert(!complete.body.toString().includes('<Error>'));
    const downloaded = await request('GET', '/large'); assert.equal(downloaded.status, 200); assert.equal(digest(downloaded.body), digest(Buffer.concat(chunks)));
    const range = await request('GET', '/large', undefined, ['--header', 'Range: bytes=1-31']); assert.equal(range.status, 206); assert.deepEqual(range.body, chunks[0].subarray(1, 32));
    results.push({ provider, version: version.stdout.trim(), cpuModel: 'Conroe', instructionEmulationOnly: true, checks: ['startup', '12-way atomic create', '12 MiB multipart SHA256', 'range'], passed: true });
    fs.writeFileSync(path.join(root, 'result.json'), JSON.stringify(results, null, 2));
    console.log(provider + ': Conroe instruction-set S3 acceptance passed');
  } finally { await stopProcessTree(server); fs.closeSync(log); }
}
