import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readFile, writeFile, rm } from 'node:fs/promises';
import path from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

const root = path.resolve(import.meta.dirname, '..');
const scratch = path.join(root, '.codex-runtime/build-information-tests');
await mkdir(scratch, { recursive: true });
const temporary = await mkdtemp(path.join(scratch, 'run-'));
const source = path.join(temporary, 'source');
const worktree = path.join(temporary, 'linked');
const execute = promisify(execFile);
const environment = { ...process.env, CARGO_HOME: process.env.CARGO_HOME || path.join(root, '.codex-runtime/cargo-home'), CARGO_TARGET_DIR: path.join(temporary, 'target') };
delete environment.EXPORTDOCMANAGER_BUILD_REVISION;
const git = async (cwd, ...args) => (await execute('git', ['-c', 'user.name=Build identity test', '-c', 'user.email=build-test@example.invalid', '-c', 'commit.gpgsign=false', '-c', `core.hooksPath=${path.join(temporary, 'no-hooks')}`, ...args], { cwd, env: environment, timeout: 30000, windowsHide: true })).stdout.trim();
async function fixture(directory) {
  const manifest = path.join(directory, 'crates/probe');
  await mkdir(path.join(manifest, 'src'), { recursive: true });
  await writeFile(path.join(manifest, 'Cargo.toml'), '[package]\nname = "build-identity-probe"\nversion = "4.5.6"\nedition = "2024"\n[workspace]\n');
  await writeFile(path.join(manifest, 'build.rs'), await readFile(path.join(root, 'crates/export-doc-engine/build.rs')));
  await writeFile(path.join(manifest, 'src/main.rs'), 'fn main() { println!("{}", env!("EXPORTDOCMANAGER_INFORMATIONAL_VERSION")); }\n');
}
const run = async (directory, revision) => (await execute('cargo', ['run', '--offline', '--quiet', '--manifest-path', 'crates/probe/Cargo.toml'], {
  cwd: directory, env: revision === undefined ? environment : { ...environment, EXPORTDOCMANAGER_BUILD_REVISION: revision }, timeout: 120000, windowsHide: true,
})).stdout.trim();
const expected = async directory => `4.5.6+git.${(await git(directory, 'rev-parse', 'HEAD')).slice(0, 12)}`;
let linked = false;
try {
  await fixture(source);
  await git(source, 'init', '--quiet');
  await git(source, 'add', 'crates');
  await git(source, 'commit', '--quiet', '-m', 'Initial source');
  assert.equal(await run(source), await expected(source));
  await git(source, 'commit', '--quiet', '--allow-empty', '-m', 'New source revision');
  assert.equal(await run(source), await expected(source), 'a commit alone must invalidate the cached build identity');
  await git(source, 'pack-refs', '--all', '--prune');
  assert.equal(await run(source), await expected(source));
  await git(source, 'commit', '--quiet', '--allow-empty', '-m', 'After packed refs');
  assert.equal(await run(source), await expected(source));
  await git(source, 'worktree', 'add', '--quiet', '--detach', worktree, 'HEAD');
  linked = true;
  assert.equal(await run(worktree), await expected(worktree));
  await git(worktree, 'commit', '--quiet', '--allow-empty', '-m', 'Detached revision');
  assert.equal(await run(worktree), await expected(worktree), 'linked worktrees track their own HEAD');
  const archive = path.join(temporary, 'archive');
  await fixture(archive);
  assert.equal(await run(archive), '4.5.6', 'an archive must not inherit the enclosing checkout identity');
  assert.equal(await run(archive, 'ABCDEF0123456789'), '4.5.6+git.abcdef012345');
  await assert.rejects(run(archive, 'not-a-commit'), error => /must be a Git commit hash/u.test(error.stderr));
  console.log('Build information passed: incremental commits, packed refs, linked/detached worktrees, source archives and explicit revisions.');
} finally {
  if (linked) await git(source, 'worktree', 'remove', '--force', worktree);
  assert.equal(path.dirname(temporary), scratch);
  await rm(temporary, { recursive: true, force: true });
}
