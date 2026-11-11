import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const root = new URL("../", import.meta.url);
const read = name => readFileSync(new URL(name, root), "utf8");
const config = JSON.parse(read("apps/export-doc-tauri/src-tauri/tauri.conf.json"));
const permissions = read("apps/export-doc-tauri/src-tauri/capabilities/main.json");
const build = read("scripts/run-tauri-build.mjs");
const runtime = read("apps/export-doc-tauri/src-tauri/src/tauri_updater_commands.rs");
assert.equal(config.app.security.dangerousDisableAssetCspModification, undefined, "Tauri CSP injection remains enabled");
assert.doesNotMatch(read("apps/export-doc-web/index.html"), /<style\b/iu, "static bootstrap CSS must be external so it does not add a style nonce to dynamic report frames");
for (const policy of [config.app.security.csp, config.app.security.devCsp]) {
  assert.match(policy, /(?:^|;\s*)style-src 'self' 'unsafe-inline'(?:;|$)/u);
  assert.match(policy, /(?:^|;\s*)script-src 'self'(?:;|$)/u);
}
assert.deepEqual(config.plugins.updater.endpoints, []);
assert.equal(config.plugins.updater.pubkey, "");
assert.deepEqual(config.bundle.windows.webviewInstallMode, { type: "downloadBootstrapper", silent: true });
const catalog = JSON.parse(read("scripts/product-editions.json"));
assert.equal(config.identifier, catalog.editions[catalog.defaultEdition].identifier);
assert.equal(config.productName, catalog.editions[catalog.defaultEdition].productName);
assert.equal(JSON.parse(permissions).remote, undefined, "release IPC must not grant external pages a desktop capability");
for (const requirement of ["TAURI_SIGNING_PRIVATE_KEY", "TAURI_SIGNING_PRIVATE_KEY_PASSWORD", "EXPORTDOCMANAGER_UPDATER_PUBLIC_KEY", "createUpdaterArtifacts: true", "EXPORTDOCMANAGER_ALLOW_INSECURE_UPDATER_ENDPOINT", "--locked"]) {
  assert.ok(build.includes(requirement), `missing updater build requirement ${requirement}`);
}
assert.ok(runtime.includes("ensure_updater_install_supported"));
assert.ok(runtime.includes("desktop_runtime::stop"));
assert.ok(!runtime.includes("sidecar::"));
assert.ok(!build.includes("dotnet"));
console.log("Tauri Rust updater trust, portable-install boundary and WebView bootstrap contracts passed.");
