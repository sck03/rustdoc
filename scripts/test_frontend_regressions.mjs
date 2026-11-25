import { readFileSync, mkdirSync, createWriteStream, writeFileSync } from 'node:fs';
import path from 'node:path';
import { finished } from 'node:stream/promises';
import { frontendRegressionSuites } from './lib/frontend-regression-suites.mjs';
import { spawnProcessTree, stopProcessTree } from './lib/child-process-tree.mjs';

const root = path.resolve(import.meta.dirname, '..');
if (!process.env.npm_execpath) throw new Error('Use npm --prefix apps/export-doc-web run test:regressions to preserve npm lifecycle context.');
const web = path.join(root, 'apps/export-doc-web');
const scripts = JSON.parse(readFileSync(path.join(web, 'package.json'), 'utf8')).scripts;
const group = process.argv[2] ?? 'all';
if (group !== 'all' && !Object.hasOwn(frontendRegressionSuites, group)) throw new Error('Unknown regression group: ' + group);
const suites = group === 'all' ? Object.values(frontendRegressionSuites).flat() : frontendRegressionSuites[group];
const commands = [...new Set(suites.flatMap(suite => {
  const command = scripts['test:' + suite];
  if (!command) throw new Error('Unregistered npm suite: ' + suite);
  return command.split(/\s*&&\s*/u).map(part => {
    const match = /^node ([\w./-]+\.mjs)$/u.exec(part);
    if (!match) throw new Error('Suite must use explicit Node entrypoints: ' + part);
    return path.resolve(web, match[1]);
  });
}))];
const output = path.join(root, 'artifacts/frontend-regressions', group);
mkdirSync(output, { recursive: true });
const results = [];
let active, cancelled = false;
const cancel = () => { cancelled = true; void stopProcessTree(active); };
process.once('SIGINT', cancel); process.once('SIGTERM', cancel);
try {
  for (const command of commands) {
    if (cancelled) break;
    const name = path.basename(command), log = createWriteStream(path.join(output, name + '.log'));
    const started = Date.now();
    console.log('[regression] ' + name);
    active = spawnProcessTree(process.execPath, [command], { cwd: web, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    active.stdout.pipe(log, { end: false }); active.stderr.pipe(log, { end: false });
    let failureTail = "";
    const captureTail = chunk => { failureTail = (failureTail + chunk.toString()).slice(-16 * 1024); };
    active.stdout.on("data", captureTail); active.stderr.on("data", captureTail);
    let timedOut = false;
    const timer = setTimeout(() => { timedOut = true; void stopProcessTree(active); }, 8 * 60_000);
    const code = await new Promise(resolve => { active.once('error', error => { log.write(String(error)); }); active.once('close', code => resolve(code ?? 1)); });
    clearTimeout(timer);
    await stopProcessTree(active); active = undefined;
    log.end(); await finished(log);
    results.push({ name, passed: code === 0 && !timedOut, timedOut, elapsedMs: Date.now() - started });
    console.log('[regression] ' + name + ': ' + (results.at(-1).passed ? 'passed' : 'FAILED'));
    if (!results.at(-1).passed) console.error(failureTail);
  }
} finally {
  await stopProcessTree(active);
  process.off('SIGINT', cancel); process.off('SIGTERM', cancel);
  writeFileSync(path.join(output, 'result.json'), JSON.stringify({ group, cancelled, results }, null, 2) + '\n');
}
if (cancelled || results.some(result => !result.passed)) process.exitCode = 1;
