import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const read = (relativePath) => readFileSync(path.join(repositoryRoot, relativePath), "utf8");
const baseConfig = JSON.parse(read("apps/export-doc-tauri/src-tauri/tauri.conf.json"));
const buildWrapper = read("scripts/run-tauri-build.mjs");
const manifestPublisher = read("scripts/publish-tauri-updater-manifest.ps1");
const releaseWorkflow = read(".github/workflows/desktop-package-reusable.yml");
const updatePage = read("apps/export-doc-web/src/features/system/UpdateCenterPage.tsx");
const desktopBridge = read("apps/export-doc-web/src/desktop/desktopBridge.ts");
const updaterCommands = read("apps/export-doc-tauri/src-tauri/src/tauri_updater_commands.rs");

assert.deepEqual(baseConfig.plugins?.updater?.endpoints, [], "base updater endpoint must remain unconfigured until release injection");
assert.equal(baseConfig.plugins?.updater?.pubkey, "", "base updater public key must remain unconfigured until release injection");
for (const requiredBuildContract of [
  "EXPORTDOCMANAGER_UPDATER_ENDPOINT",
  "EXPORTDOCMANAGER_UPDATER_PUBLIC_KEY",
  "TAURI_SIGNING_PRIVATE_KEY",
  "TAURI_SIGNING_PRIVATE_KEY_PASSWORD",
  "createUpdaterArtifacts: true",
  'parsedEndpoint.protocol !== "https:"',
]) {
  assert.ok(buildWrapper.includes(requiredBuildContract), `release build wrapper is missing ${requiredBuildContract}`);
}
for (const requiredWorkflowContract of [
  "EXPORTDOCMANAGER_REQUIRE_SIGNED_UPDATER",
  "Build signed updater package",
  "TAURI_SIGNING_PRIVATE_KEY_PASSWORD",
  "Merge signed updater manifest into GitHub Release",
  "publish-tauri-updater-manifest.ps1",
]) {
  assert.ok(releaseWorkflow.includes(requiredWorkflowContract), `desktop release workflow is missing ${requiredWorkflowContract}`);
}
for (const signaturePattern of ["*-setup.exe.sig", "*.AppImage.sig", "*.app.tar.gz.sig"]) {
  assert.ok(manifestPublisher.includes(signaturePattern), `updater manifest publisher is missing ${signaturePattern}`);
}
for (const manifestContract of ["latest.json", "platforms", "signature", "releases/download"]) {
  assert.ok(manifestPublisher.includes(manifestContract), `updater manifest publisher is missing ${manifestContract}`);
}

assert.ok(updatePage.includes("更新地址和签名公钥不可在页面修改"));
assert.doesNotMatch(updatePage, /<input[^>]+(?:endpoint|publicKey)/iu, "update trust roots must not be editable inputs");
assert.doesNotMatch(desktopBridge, /endpoint\??:|publicKey\??:/u, "desktop bridge must not accept runtime updater trust roots");
assert.doesNotMatch(updaterCommands, /endpoint:\s*Option|public_key:\s*Option/u, "Rust updater commands must only use packaged trust roots");

process.stdout.write("Tauri updater release trust contract passed.\n");
