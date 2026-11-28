import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import net from 'node:net';
import { randomBytes, randomUUID } from 'node:crypto';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { cargoArtifactDirectory } from './cargo-paths.mjs';
import { officeReviewInvoke, officeReviewOperations } from './native-office-ui.mjs';
import { spawnProcessTree, stopProcessTree } from './child-process-tree.mjs';

export async function postgresS3Host(repo, name, index) {
  const output = path.join(repo, 'artifacts', 'postgres-s3-' + name, randomUUID());
  const app = path.join(output, 'App'), runtime = path.join(output, 'Runtime');
  const source = path.join(output, 'Source'), target = path.join(output, 'ColdRestore');
  for (const dir of [app, runtime, source, target]) fs.mkdirSync(dir, { recursive: true, mode: 0o700 });
  for (const dir of ['Templates', 'Resources/Fonts/OpenSource', 'Resources/ExcelTemplates']) fs.cpSync(path.join(repo, dir), path.join(app, dir), { recursive: true });
  const tools = path.join(app, 'Tools/PostgreSQL/bin'); fs.mkdirSync(tools, { recursive: true });
  for (const entry of fs.readdirSync(process.env.EXPORTDOC_TEST_PG_BIN, { withFileTypes: true })) {
    if (entry.isFile() && (/^(pg_dump|pg_restore|psql)(\.exe)?$/u.test(entry.name) || entry.name.endsWith('.dll'))) fs.copyFileSync(path.join(process.env.EXPORTDOC_TEST_PG_BIN, entry.name), path.join(tools, entry.name));
  }
  const spec = JSON.parse(fs.readFileSync(path.join(repo, 'crates/export-doc-contracts/src/openapi.json'), 'utf8'));
  const operations = officeReviewOperations(spec), secrets = [];
  const bootstrap = randomBytes(32).toString('hex');
  const credentials = { username: 'admin', password: 'S3-' + randomBytes(16).toString('hex') };
  const writeSecret = (name, value) => { const file = path.join(runtime, name); fs.writeFileSync(file, value, { mode: 0o600 }); secrets.push(file); return file; };
  const baseEnvironment = { ...process.env };
  for (const key of Object.keys(baseEnvironment)) if (/^EXPORTDOCMANAGER_(POSTGRES_|BOOTSTRAP_|MAINTENANCE_|MASTER_KEY|FILE_STORE_ROOTS)/u.test(key)) delete baseEnvironment[key];
  const environments = {};
  for (const mode of ['SOURCE', 'TARGET']) {
    const prefix = 'S3_' + mode + '_' + index;
    const env = { ...baseEnvironment, EXPORTDOCMANAGER_POSTGRES_OWNER: 'native_owner', EXPORTDOCMANAGER_BUSINESS_TIME_ZONE: 'Asia/Shanghai' };
    for (const [role, variable] of [['APP', 'EXPORTDOCMANAGER_POSTGRES_CONNECTION_FILE'], ['MAINTENANCE', 'EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_CONNECTION_FILE']]) {
      const value = process.env['EXPORTDOC_TEST_' + prefix + '_' + role];
      assert(value && /(?:^|\s)host=127\.0\.0\.1(?:\s|$)/u.test(value) && value.includes('dbname=native_s3_' + mode.toLowerCase() + '_' + index + ' '), 'Use the isolated PostgreSQL S3 runner');
      env[variable] = writeSecret(mode + '-' + role + '.txt', value);
    }
    env.EXPORTDOCMANAGER_BOOTSTRAP_TOKEN_FILE = writeSecret(mode + '-bootstrap.txt', bootstrap);
    environments[mode] = env;
  }
  const executable = path.join(cargoArtifactDirectory(repo, process.env, process.env.EXPORTDOC_TEST_CONFIGURATION === 'Release' ? 'release' : 'debug'), 'export-doc-server' + (process.platform === 'win32' ? '.exe' : ''));
  const execute = promisify(execFile); let server, log, sequence = 0;
  const stop = async () => { if (server) await stopProcessTree(server, 5000); server = undefined; if (log !== undefined) fs.closeSync(log); log = undefined; };
  const start = async (mode, maintenance = false) => {
    await stop(); const data = mode === 'SOURCE' ? source : target;
    const env = { ...environments[mode] };
    if (maintenance) env.EXPORTDOCMANAGER_MAINTENANCE_TOKEN_FILE = writeSecret('maintenance-token.txt', bootstrap);
    else delete env.EXPORTDOCMANAGER_POSTGRES_MAINTENANCE_CONNECTION_FILE;
    const listener = net.createServer(); await new Promise(resolve => listener.listen(0, '127.0.0.1', resolve));
    const port = listener.address().port; await new Promise(resolve => listener.close(resolve));
    const url = 'http://127.0.0.1:' + port;
    log = fs.openSync(path.join(output, 'server-' + (++sequence) + '.log'), 'a');
    server = spawnProcessTree(executable, ['--app-root', app, '--data-root', data, '--web-root', path.join(repo, 'apps/export-doc-web/dist'), '--bind', '127.0.0.1:' + port, ...(maintenance ? ['--maintenance-web'] : [])], { cwd: repo, env, windowsHide: true, stdio: ['ignore', log, log] });
    const deadline = Date.now() + 60000; let ready = false;
    while (Date.now() < deadline) {
      assert(server.exitCode === null, 'S3 test host exited; inspect its isolated log');
      try { const r = await fetch(url, { signal: AbortSignal.timeout(1000) }); if (r.ok) { ready = true; break; } } catch {}
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert(ready, 'S3 test host startup timed out');
    return { repo, output, data, url, operations, credentials, invoke: officeReviewInvoke(url, spec, operations) };
  };
  const close = async () => { await stop(); for (const file of secrets) fs.rmSync(file, { force: true }); };
  try {
    await execute(executable, ['--app-root', app, '--data-root', source, '--initialize-only'], { cwd: repo, env: environments.SOURCE, windowsHide: true, timeout: 120000 });
    const fixture = await start('SOURCE');
    await officeReviewInvoke(fixture.url, spec, operations, { 'X-ExportDocManager-Bootstrap-Token': bootstrap })('Login', credentials);
    return { ...fixture, source, target, bootstrap, spec, start, stop, close };
  } catch (error) { await close(); throw error; }
}
