import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import http from 'node:http';
import { createHash } from 'node:crypto';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

const repo = path.resolve(import.meta.dirname, '../../../..');
const npmCli = process.env.npm_execpath;
assert(npmCli, 'Run through the npm test:font-build entry');
const production = JSON.parse(await fs.readFile(path.join(repo, 'apps/export-doc-web/package.json'), 'utf8'));
const dockerfile = await fs.readFile(path.join(repo, 'deploy/rust-native/Dockerfile'), 'utf8');
const ignore = await fs.readFile(path.join(repo, '.dockerignore'), 'utf8');
const buildIndex = dockerfile.indexOf('RUN npm --prefix apps/export-doc-web run build');
for (const required of ['scripts/provision-report-fonts.mjs', 'scripts/lib/verified-download.mjs', 'Resources/Fonts/OpenSource/font-manifest.json']) {
  assert(dockerfile.indexOf(`COPY ${required}`) >= 0 && dockerfile.indexOf(`COPY ${required}`) < buildIndex, `${required}: available before the Docker frontend build`);
  assert(ignore.split(/\r?\n/u).includes(`!${required}`), `${required}: included in the build context`);
}
await fs.mkdir(path.join(repo, '.codex-runtime'), { recursive: true });
const root = await fs.mkdtemp(path.join(repo, '.codex-runtime/font-build-'));
const packageRoot = path.join(root, 'apps/export-doc-web');
const fontRoot = path.join(root, 'Resources/Fonts/OpenSource');
const bytes = Buffer.from('controlled font provisioning lifecycle fixture');
const sha256 = createHash('sha256').update(bytes).digest('hex');
let requests = 0;
const server = http.createServer((request, response) => { requests++; response.end(bytes); });
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
try {
  for (const directory of [packageRoot, fontRoot, path.join(root, 'scripts/lib')]) await fs.mkdir(directory, { recursive: true });
  for (const file of ['scripts/provision-report-fonts.mjs', 'scripts/lib/verified-download.mjs']) await fs.copyFile(path.join(repo, file), path.join(root, file));
  const font = { fileName: 'fixture.otf', sha256, url: `http://127.0.0.1:${server.address().port}/font` };
  const manifest = path.join(fontRoot, 'font-manifest.json');
  const writeManifest = async value => fs.writeFile(manifest, JSON.stringify({ schemaVersion: 1, fonts: [value] }));
  await writeManifest(font);
  await fs.writeFile(path.join(packageRoot, 'package.json'), JSON.stringify({ private: true, type: 'module', scripts: { prebuild: production.scripts.prebuild, build: 'node ../../scripts/build-marker.mjs' } }));
  await fs.writeFile(path.join(root, 'scripts/build-marker.mjs'), `import fs from 'node:fs'; assertFont(); function assertFont(){if(!fs.existsSync('../../Resources/Fonts/OpenSource/fixture.otf'))throw Error('font preparation skipped');} fs.appendFileSync('../../build-marker.txt','built\\n');`);
  const execute = promisify(execFile);
  const run = () => execute(process.execPath, [npmCli, '--prefix', packageRoot, 'run', 'build'], { cwd: root, windowsHide: true, timeout: 60000, env: { ...process.env, npm_config_cache: path.join(root, 'npm-cache') } });
  await assert.rejects(fs.access(path.join(fontRoot, font.fileName)));
  await run();
  assert.deepEqual(await fs.readFile(path.join(fontRoot, font.fileName)), bytes, 'A clean build provisions the verified font first');
  assert.equal(requests, 1);
  await run();
  assert.equal(requests, 1, 'Subsequent builds reuse the verified file');
  const marker = await fs.readFile(path.join(root, 'build-marker.txt'), 'utf8');
  await writeManifest({ ...font, sha256: '0'.repeat(64) });
  await assert.rejects(run(), /SHA-256/iu);
  assert.equal(await fs.readFile(path.join(root, 'build-marker.txt'), 'utf8'), marker, 'Invalid provisioning prevents the frontend build');
  assert.deepEqual(await fs.readFile(path.join(fontRoot, font.fileName)), bytes, 'Failure preserves the existing font');
  console.log('Font build lifecycle passed: clean preparation, cache reuse, fail-closed build and Docker inputs');
} finally {
  await new Promise(resolve => server.close(resolve));
  const actual = await fs.realpath(root), workspace = await fs.realpath(repo);
  assert(actual.startsWith(workspace + path.sep) && !((await fs.lstat(root)).isSymbolicLink()), 'Cleanup stays inside the managed fixture root');
  await fs.rm(root, { recursive: true, force: true });
}
