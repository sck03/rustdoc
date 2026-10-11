import assert from "node:assert/strict";
import { readFile, rm } from "node:fs/promises";
import path from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { syncVersion } from "../sync-version.mjs";

export async function verifyProductVersionContracts(root, temporary, write) {
  // Exercise version synchronization without touching the working tree or resolving dependencies.
  await write("version.json", '{"version":"0.1.1"}\n');
  await write("Cargo.toml", '[workspace]\nmembers = ["crates/core", "apps/export-doc-tauri/src-tauri"]\n[workspace.package]\nversion = "0.1.0"\n');
  await write("crates/core/Cargo.toml", '[package]\nname = "core"\nversion.workspace = true\n');
  const localPackages = [
    ["apps/export-doc-tauri/src-tauri", "export-doc-tauri"],
    ["apps/exportdoc-ocr-rs", "exportdoc-ocr"],
    ["tools/excel-analyzer-rs", "exportdoc-excel-analyzer"],
  ];
  const block = name => `[[package]]\nname = "${name}"\nversion = "0.1.1"\n`;
  for (const [directory, name] of localPackages) {
    await write(`${directory}/Cargo.toml`, `[package]\nname = "${name}"\nversion = "0.1.1"\n`);
    if (name !== "export-doc-tauri") await write(`${directory}/Cargo.lock`, block(name));
  }
  const registry = '[[package]]\nname = "registry-crate"\nversion = "1.0.0"\nsource = "registry+https://github.com/rust-lang/crates.io-index"\nchecksum = "keep-me"\n';
  await write("Cargo.lock", block("core") + block("export-doc-tauri") + block("exportdoc-excel-analyzer") + registry);
  for (const directory of ["apps/export-doc-web", "apps/export-doc-tauri"]) {
    await write(`${directory}/package.json`, '{"version":"0.1.1"}');
    await write(`${directory}/package-lock.json`, '{"version":"0.1.1","packages":{"":{"version":"0.1.1"}}}');
  }
  await write("apps/export-doc-tauri/src-tauri/tauri.conf.json", '{"version":"0.1.1"}');
  const originalWorkspace = await readFile(path.join(temporary, "Cargo.toml"), "utf8");
  await assert.rejects(syncVersion(temporary, undefined, { check: true }), /out of sync: Cargo.toml/u);
  assert.equal(await readFile(path.join(temporary, "Cargo.toml"), "utf8"), originalWorkspace, "checking must never repair drift silently");
  assert.equal(await syncVersion(temporary, "v2.3.4-beta.1"), "2.3.4-beta.1");
  const lock = await readFile(path.join(temporary, "Cargo.lock"), "utf8");
  assert.ok(lock.includes(registry), "third-party versions and checksums must stay exact");
  assert.equal(lock.match(/version = "2.3.4-beta.1"/gu).length, 3);
  assert.deepEqual(JSON.parse(await readFile(path.join(temporary, "version.json"))), { version: "2.3.4-beta.1" }, "product version has no retired assembly metadata");
  assert.equal(JSON.parse(await readFile(path.join(temporary, "apps/export-doc-web/package-lock.json"))).packages[""].version, "2.3.4-beta.1");
  await syncVersion(temporary);
  assert.equal(await readFile(path.join(temporary, "Cargo.lock"), "utf8"), lock, "idempotent sync");
  assert.equal(await syncVersion(temporary, undefined, { check: true }), "2.3.4-beta.1");
  const compact = '{"version":"2.3.4-beta.1","packages":{"":{"version":"2.3.4-beta.1"}}}';
  await write("apps/export-doc-web/package-lock.json", compact);
  await syncVersion(temporary, undefined, { check: true });
  assert.equal(await readFile(path.join(temporary, "apps/export-doc-web/package-lock.json"), "utf8"), compact, "formatting alone is not version drift");
  await write("apps/export-doc-web/package-lock.json", compact.replace('"packages":{"":{"version":"2.3.4-beta.1"', '"packages":{"":{"version":"2.3.3"'));
  await assert.rejects(syncVersion(temporary, undefined, { check: true }), /apps\/export-doc-web\/package-lock.json/u);
  await syncVersion(temporary);
  for (const file of ['sync-version.mjs', 'lib/release-version.mjs']) await write(`scripts/${file}`, await readFile(path.join(root, 'scripts', file)));
  await write('apply-version.ps1', `param($Helper, $RepositoryRoot, $Version)\n$ErrorActionPreference = 'Stop'\n. $Helper\nInitialize-ExportDocProductVersion -RepositoryRoot $RepositoryRoot -Version $Version\n`);
  const applyVersion = version => promisify(execFile)('pwsh', ['-NoProfile', '-NonInteractive', '-File', path.join(temporary, 'apply-version.ps1'), '-Helper', path.join(root, 'scripts/lib/build-script-support.ps1'), '-RepositoryRoot', temporary, '-Version', version], { cwd: temporary, timeout: 30000, windowsHide: true });
  await applyVersion('v3.4.5-beta.2');
  assert.equal(JSON.parse(await readFile(path.join(temporary, 'version.json'))).version, '3.4.5-beta.2', 'the build parameter controls all product manifests');
  await syncVersion(temporary, '3.4.5-beta.2', { check: true });
  await assert.rejects(applyVersion('3.4.5+unsupported'));
  assert.equal(JSON.parse(await readFile(path.join(temporary, 'version.json'))).version, '3.4.5-beta.2', 'invalid build versions cannot mutate the source');
  await applyVersion('2.3.4-beta.1');
  await rm(path.join(temporary, "apps/exportdoc-ocr-rs/Cargo.lock"));
  await assert.rejects(syncVersion(temporary, "3.0.0"));
  assert.equal(JSON.parse(await readFile(path.join(temporary, "version.json"))).version, "2.3.4-beta.1", "validate all files before writing any version");
}
