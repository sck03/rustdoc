import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
const root = path.resolve(import.meta.dirname, "..");
mkdirSync(path.join(root, ".codex-runtime"), { recursive: true });
const fixture = mkdtempSync(path.join(root, ".codex-runtime/dependency-policy-"));
const run = (...args) => spawnSync(process.execPath, [path.join(root, "scripts/verify-dependency-policy.mjs"), "--repository-root", fixture, ...args], { encoding: "utf8", timeout: 30000 });
try {
  writeFileSync(path.join(fixture, "Cargo.toml"), "[workspace]\n");
  assert.equal(run().status, 0);
  for (const name of ["old.cs", "old.csproj", "global.json", "packages.lock.json"]) {
    writeFileSync(path.join(fixture, name), "{}");
    assert.notEqual(run().status, 0, name);
    assert.equal(run("--generated-only").status, 0);
    rmSync(path.join(fixture, name));
  }
  mkdirSync(path.join(fixture, "target"));
  writeFileSync(path.join(fixture, "target/cache.cs"), "generated");
  assert.equal(run().status, 0);
  writeFileSync(path.join(fixture, "THIRD_PARTY_DEPENDENCIES.md"), "| NPOI | 2.7.6 |");
  assert.notEqual(run("--generated-only").status, 0);
  console.log("Rust dependency boundary regression passed.");
} finally { rmSync(fixture, { recursive: true, force: true }); }
