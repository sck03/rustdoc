import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const read = (relativePath) => readFileSync(path.join(repositoryRoot, relativePath), "utf8");
const baseConfig = JSON.parse(read("apps/export-doc-tauri/src-tauri/tauri.conf.json"));
const editionCatalog = JSON.parse(read("scripts/product-editions.json"));
const buildWrapper = read("scripts/run-tauri-build.mjs");
const manifestPublisher = read("scripts/publish-tauri-updater-manifest.ps1");
const releaseWorkflow = read(".github/workflows/desktop-package-reusable.yml");
const serverReleaseWorkflow = read(".github/workflows/browser-server-package-reusable.yml");
const updatePage = read("apps/export-doc-web/src/features/system/UpdateCenterPage.tsx");
const desktopBridge = read("apps/export-doc-web/src/desktop/desktopBridge.ts");
const updaterCommands = read("apps/export-doc-tauri/src-tauri/src/tauri_updater_commands.rs");
const settingsPanel = read("apps/export-doc-web/src/features/settings/RuntimeDatabaseSettingsPanel.tsx");
const settingsModel = read("src/ExportDocManager.Application/Models/Configuration/AppSettings.cs");

assert.deepEqual(baseConfig.plugins?.updater?.endpoints, [], "base updater endpoint must remain unconfigured until release injection");
assert.equal(baseConfig.plugins?.updater?.pubkey, "", "base updater public key must remain unconfigured until release injection");
assert.equal(baseConfig.identifier, "com.exportdocmanager.desktop.full", "the default Tauri identifier must belong to Full edition");
assert.equal(editionCatalog.schemaVersion, 1);
const editionEntries = Object.entries(editionCatalog.editions || {});
assert.deepEqual(editionEntries.map(([name]) => name).sort(), ["Document", "Full", "Sales"]);
assert.equal(new Set(editionEntries.map(([, value]) => value.identifier)).size, 3, "each product edition requires a unique identifier");
assert.equal(new Set(editionEntries.map(([, value]) => value.releaseTagPrefix)).size, 3, "each product edition requires a unique release tag prefix");
assert.equal(new Set(editionEntries.map(([, value]) => value.stableManifestAsset)).size, 3, "each product edition requires a unique stable manifest");
assert.equal(new Set(editionEntries.map(([, value]) => value.stableChannelTag)).size, 3, "each product edition requires a unique stable channel tag");
for (const requiredBuildContract of [
  "product-editions.json",
  "editionMetadata.productName",
  "editionMetadata.identifier",
  "EXPORTDOCMANAGER_UPDATER_ENDPOINT",
  "EXPORTDOCMANAGER_UPDATER_PUBLIC_KEY",
  "TAURI_SIGNING_PRIVATE_KEY",
  "TAURI_SIGNING_PRIVATE_KEY_PASSWORD",
  "createUpdaterArtifacts: true",
  "EXPORTDOCMANAGER_ALLOW_INSECURE_UPDATER_ENDPOINT",
  'dangerousInsecureTransportProtocol: endpoint.startsWith("http:")',
  "endpoint ? [endpoint] : []",
  "releases/download",
  'spawnSync(process.execPath, [tauriCliPath, "build"',
]) {
  assert.ok(buildWrapper.includes(requiredBuildContract), `release build wrapper is missing ${requiredBuildContract}`);
}
assert.ok(!buildWrapper.includes("npm.cmd"), "the build wrapper must not spawn a Windows command shim from Node");
for (const requiredWorkflowContract of [
  "EXPORTDOCMANAGER_REQUIRE_SIGNED_UPDATER",
  "Build signed updater package",
  "TAURI_SIGNING_PRIVATE_KEY_DOCUMENT",
  "TAURI_SIGNING_PRIVATE_KEY_SALES",
  "TAURI_SIGNING_PRIVATE_KEY_FULL",
  "Publish immutable edition release and update channel",
  "publish-tauri-updater-manifest.ps1",
  "-Edition ${{ inputs.edition }}",
]) {
  assert.ok(releaseWorkflow.includes(requiredWorkflowContract), `desktop release workflow is missing ${requiredWorkflowContract}`);
}
assert.doesNotMatch(releaseWorkflow, /WINDOWS_SIGNING_CERTIFICATE|APPLE_CERTIFICATE/u, "commercial OS signing is not mandatory before commercial release");
assert.doesNotMatch(releaseWorkflow, /gh release upload[^\r\n]*--clobber/iu, "immutable desktop release assets must never be overwritten");
assert.doesNotMatch(serverReleaseWorkflow, /gh release upload[^\r\n]*--clobber/iu, "immutable server release assets must never be overwritten");
for (const signaturePattern of ["*-setup.exe.sig", "*.AppImage.sig", "*.app.tar.gz.sig"]) {
  assert.ok(manifestPublisher.includes(signaturePattern), `updater manifest publisher is missing ${signaturePattern}`);
}
for (const manifestContract of [
  "product-editions.json",
  "releaseTagPrefix",
  "stableChannelTag",
  "prereleaseChannelTag",
  "platforms",
  "signature",
  "releases/download",
  "Immutable release",
  "Publish-ChannelManifestAtomically",
]) {
  assert.ok(manifestPublisher.includes(manifestContract), `updater manifest publisher is missing ${manifestContract}`);
}
assert.doesNotMatch(manifestPublisher, /gh release upload[^\r\n]*--clobber/iu, "formal version assets must not use --clobber");

for (const hiddenUpdateDetail of [
  "更新配置",
  "管理员统一控制更新来源",
  "当前更新地址",
  "地址来源",
  "传输方式",
  "目标平台",
  "签名信任",
  "签名公钥固定在安装包内",
  "下载地址",
  "验证方式",
  "重启策略",
  "打开系统设置",
]) {
  assert.ok(!updatePage.includes(hiddenUpdateDetail), `update center must hide administrator detail: ${hiddenUpdateDetail}`);
}
assert.doesNotMatch(updatePage, /<input[^>]+(?:endpoint|publicKey)/iu, "update center must not expose a duplicate updater editor");
assert.ok(settingsPanel.includes('path={systemUpdaterEndpointPath}'), "administrator settings must expose the updater endpoint");
assert.match(settingsPanel, /HTTP 地址[^。]*受控公司内网|受控公司内网[^。]*HTTP 地址/u, "administrator settings must explain trusted intranet HTTP use");
assert.doesNotMatch(settingsPanel, /publicKey|public_key/iu, "administrator settings must not expose the updater public key");
assert.ok(settingsModel.includes("UpdaterEndpoint"), "system settings must persist the administrator endpoint");
assert.match(desktopBridge, /checkTauriUpdate\(endpoint\?: string\)/u, "desktop bridge must accept an optional endpoint override");
assert.match(desktopBridge, /installTauriUpdate\(endpoint\?: string\)/u, "install must reuse the optional endpoint override");
assert.doesNotMatch(desktopBridge, /publicKey\??:|public_key\??:/u, "desktop bridge must not accept a runtime updater public key");
assert.match(updaterCommands, /endpoint:\s*Option<String>/u, "Rust updater commands must accept the administrator endpoint");
assert.ok(updaterCommands.includes(".endpoints(vec![endpoint])"), "Rust updater builder must apply the endpoint override");
assert.doesNotMatch(updaterCommands, /public_key:\s*Option|\.pubkey\(/u, "Rust updater commands must keep the packaged public key fixed");
assert.ok(updaterCommands.includes('parsed.scheme() != "http" && parsed.scheme() != "https"'));

process.stdout.write("Tauri updater release trust contract passed.\n");
