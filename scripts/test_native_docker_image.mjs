// Real daemon validation for the image-only installation shown in the user guide.
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { randomUUID } from 'node:crypto';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const output = path.join(repo, 'artifacts/docker-image-check', randomUUID());
await fs.mkdir(output, { recursive: true, mode: 0o700 });
const image = 'exportdoc-rust-native:local';
const project = 'exportdoc-image-check-' + randomUUID().slice(0, 8);
const composePath = path.join(output, 'compose.yml');
const compose = ['compose', '--project-name', project, '--file', composePath];
const execute = promisify(execFile);
const env = { ...process.env, NATIVE_PORT: '5196', NATIVE_BIND_ADDRESS: '127.0.0.1', EXPORTDOC_IMAGE: image };
async function docker(args, timeout = 240000) {
  return (await execute('docker', args, { cwd: output, env, timeout, windowsHide: true, maxBuffer: 4 * 1024 * 1024 })).stdout;
}
async function verify(flags = []) {
  const result = await execute(process.execPath, [path.join(repo, 'scripts/verify-native-docker.mjs'),
    '--url', 'http://127.0.0.1:5196', '--runtime', output, '--isolated-validation', '--compose', composePath, '--project', project, ...flags],
  { cwd: repo, env, timeout: 240000, windowsHide: true, maxBuffer: 4 * 1024 * 1024 });
  process.stdout.write(result.stdout);
}
async function cleanupCompletedSetup() {
  for (const service of ['setup', 'initialize']) {
    const id = (await docker([...compose, 'ps', '--all', '--quiet', service])).trim();
    const [container] = JSON.parse(await docker(['inspect', id]));
    assert.equal(container.Name, `/${project}-${service}`);
    assert.equal(container.State.Status, 'exited');
    assert.equal(container.State.ExitCode, 0);
  }
  await docker([...compose, 'rm', '--force', 'setup', 'initialize']);
  const ids = (await docker([...compose, 'ps', '--all', '--quiet'])).trim().split(/\s+/u);
  const containers = JSON.parse(await docker(['inspect', ...ids]));
  assert.deepEqual(containers.map(item => item.Name).sort(), [`/${project}-application`, `/${project}-postgres`]);
  assert(containers.every(item => item.State.Health.Status === 'healthy'));
}
await fs.writeFile(composePath, await docker(['run', '--rm', image, '--print-compose', image]));
try {
  await docker([...compose, 'up', '-d', '--wait', '--wait-timeout', '180']);
  const token = await docker([...compose, 'exec', '-T', 'application', 'cat', '/run/exportdoc/bootstrap-token.txt']);
  await fs.writeFile(path.join(output, 'bootstrap-token.txt'), token, { mode: 0o600 });
  await fs.writeFile(path.join(output, 'native-runtime.json'), await docker([...compose, 'exec', '-T', 'application', 'cat', '/run/exportdoc/deployment.json']), { mode: 0o600 });
  await verify();
  await cleanupCompletedSetup();
  await verify(['--resume', '--stage-restore']);
  await docker([...compose, 'stop', 'application']);
  await docker([...compose, 'run', '--rm', '--no-deps', 'restore']);
  await docker([...compose, 'up', '-d', '--no-deps', '--wait', 'application']);
  await verify(['--resume']);
  await docker([...compose, 'down']);
  await docker([...compose, 'up', '-d', '--wait', '--wait-timeout', '180']);
  const reused = await docker([...compose, 'exec', '-T', 'application', 'cat', '/run/exportdoc/bootstrap-token.txt']);
  assert.equal(reused, token, 'restart must reuse the same deployment identity');
  await verify(['--resume']);
  await cleanupCompletedSetup();
  console.log('Image-only install, separate credentials, database restore and persistent restart passed.');
} finally {
  await docker([...compose, 'down', '--volumes', '--remove-orphans']);
}
