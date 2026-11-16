import assert from 'node:assert/strict';
import path from 'node:path';
import readline from 'node:readline';
import { spawnProcessTree, stopProcessTree } from './child-process-tree.mjs';

// RSS sums may include shared pages more than once; sampled peaks are lower bounds.
export async function measureProcessTree(pid, run) {
  assert.equal(process.platform, 'win32', 'This sampler records Windows process memory');
  const child = spawnProcessTree('python', [path.join(import.meta.dirname, 'windows-process-memory.py'), String(pid), '0.05'],
    { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
  const samples = []; let error = '', failure, stopping = false;
  child.stderr.on('data', bytes => { error += bytes; });
  const lines = readline.createInterface({ input: child.stdout });
  let ready, rejectFirst;
  const first = new Promise((resolve, reject) => {
    ready = resolve; rejectFirst = reject;
  });
  const fail = cause => { failure ??= cause; rejectFirst(cause); };
  child.once('error', fail);
  child.stdin.on('error', fail);
  const closed = new Promise(resolve => child.once('close', code => {
    if (!stopping || code !== 0 || !samples.length) fail(new Error(`Memory sampler exited ${code}: ${error}`));
    resolve();
  }));
  lines.on('line', line => {
    try {
      const sample = JSON.parse(line);
      assert(sample.processes.some(process => process.pid === pid), 'Measured process is unavailable');
      samples.push(sample); ready();
    } catch (cause) { fail(cause); }
  });
  async function bounded(promise) {
    let timer;
    try { return await Promise.race([promise, new Promise((_, reject) => {
      timer = setTimeout(() => reject(new Error('Memory sampler timed out')), 10000);
    })]); } finally { clearTimeout(timer); }
  }
  try {
    await bounded(first);
    const started = performance.now();
    const result = await run();
    const elapsedMs = performance.now() - started;
    if (failure) throw failure;
    stopping = true; child.stdin.end('\n'); await bounded(closed);
    if (failure) throw failure;
    assert.equal(error, ''); assert(samples.length > 0);
    const sum = (sample, field) => sample.processes.reduce((total, process) => total + process[field], 0);
    return { elapsedMs, result, sampleIntervalMs: 50, sampleCount: samples.length,
      baselineRss: sum(samples[0], 'rss'), finalRss: sum(samples.at(-1), 'rss'),
      peakRss: Math.max(...samples.map(sample => sum(sample, 'rss'))),
      peakPrivate: Math.max(...samples.map(sample => sum(sample, 'private'))),
      maxProcesses: Math.max(...samples.map(sample => sample.processes.length)) };
  } finally { lines.close(); await stopProcessTree(child); }
}
