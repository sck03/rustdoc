import assert from "node:assert/strict";
import { mkdtemp, mkdir, writeFile, readFile, rm } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createHash } from "node:crypto";
import { createServer } from "node:http";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { normalizeReleaseVersion } from "./lib/release-version.mjs";
import { createReleasePlan } from "./lib/native-release-plan.mjs";
import { syncVersion } from "./sync-version.mjs";
import { promoteContainerRelease } from "./lib/container-release.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const scratch = path.join(root, ".codex-runtime", "release-contract-tests");
await mkdir(scratch, { recursive: true });
const temporary = await mkdtemp(path.join(scratch, "run-"));
const write = async (name, content) => {
  const file = path.join(temporary, name);
  await mkdir(path.dirname(file), { recursive: true });
  await writeFile(file, content);
};
try {
  assert.equal(normalizeReleaseVersion(" v2.3.4-beta.1 "), "2.3.4-beta.1");
  for (const invalid of ["", "1.2", "01.2.3", "1.2.3-beta.01", "1.2.3+build", "1.2.3\nX=1", "1.2.3-", "65536.0.0", "$(echo 1.2.3)"]) {
    assert.throws(() => normalizeReleaseVersion(invalid));
  }
  const base = { product: "desktop", os: "all", architecture: "all", version: "v2.3.4", repository: "Owner/RustDoc" };
  const plan = createReleasePlan(base);
  assert.equal(plan.matrix.include.length, 5);
  assert.equal(plan.image, "ghcr.io/owner/exportdoc-rust-native");
  assert.deepEqual(createReleasePlan({ ...base, product: "container", os: "linux" }).matrix.include.map(item => item.platform), ["linux/amd64", "linux/arm64"]);
  assert.deepEqual(createReleasePlan({ ...base, product: "web" }).matrix.include.map(item => item.artifact), ["windows-x64", "linux-x64", "linux-arm64", "macos-arm64"]);
  assert.equal(createReleasePlan({ ...base, os: "linux", architecture: "arm64" }).matrix.include[0].runner, "ubuntu-24.04-arm");
  for (const selection of [{ os: "macos", architecture: "x64" }, { product: "web", os: "windows", architecture: "arm64" }, { product: "container", os: "windows" }]) {
    assert.throws(() => createReleasePlan({ ...base, ...selection }));
  }

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
  await write("Directory.Build.props", "C# comparison must stay unchanged");
  assert.equal(await syncVersion(temporary, "v2.3.4-beta.1"), "2.3.4-beta.1");
  const lock = await readFile(path.join(temporary, "Cargo.lock"), "utf8");
  assert.ok(lock.includes(registry), "third-party versions and checksums must stay exact");
  assert.equal(lock.match(/version = "2.3.4-beta.1"/gu).length, 3);
  assert.equal(await readFile(path.join(temporary, "Directory.Build.props"), "utf8"), "C# comparison must stay unchanged");
  assert.equal(JSON.parse(await readFile(path.join(temporary, "apps/export-doc-web/package-lock.json"))).packages[""].version, "2.3.4-beta.1");
  await syncVersion(temporary);
  assert.equal(await readFile(path.join(temporary, "Cargo.lock"), "utf8"), lock, "idempotent sync");
  await rm(path.join(temporary, "apps/exportdoc-ocr-rs/Cargo.lock"));
  await assert.rejects(syncVersion(temporary, "3.0.0"));
  assert.equal(JSON.parse(await readFile(path.join(temporary, "version.json"))).version, "2.3.4-beta.1", "validate all files before writing any version");

  await testPublication();
  testContainerPromotion();
  console.log("Native release version, architecture, lock preservation and publication contracts passed.");
} finally {
  // Only the freshly allocated test directory may be removed.
  assert.equal(path.dirname(temporary), scratch);
  await rm(temporary, { recursive: true, force: true });
}

function testContainerPromotion() {
  const image = "ghcr.io/owner/exportdoc-rust-native";
  const revision = "a".repeat(40);
  const records = ["x64", "arm64"].map((architecture, index) => ({ architecture, image, revision,
    version: "2.3.4", digest: `sha256:${String(index + 1).repeat(64)}` }));
  const desired = JSON.stringify({ manifests: records.map(record => ({ digest: record.digest,
    platform: { os: "linux", architecture: record.architecture === "x64" ? "amd64" : "arm64" } })) });
  let existing = null;
  const mutations = [];
  const docker = args => {
    if (args.includes("--dry-run")) return desired;
    if (args.includes("inspect")) return existing;
    mutations.push(args);
    existing = desired;
    return "";
  };
  const options = { image, revision, requestedVersion: "v2.3.4", selection: "all", publishLatest: true, records, docker };
  assert.equal(promoteContainerRelease(options).reference, `${image}:2.3.4`);
  assert.equal(mutations.length, 2, "version and latest publish only after receipt verification");
  mutations.length = 0;
  promoteContainerRelease({ ...options, publishLatest: false });
  assert.equal(mutations.length, 0, "identical version index is immutable on retry");
  assert.throws(() => promoteContainerRelease({ ...options, selection: "x64", records: records.slice(0, 1) }), /latest/u);
  assert.throws(() => promoteContainerRelease({ ...options, requestedVersion: "2.3.4-beta.1" }), /latest/u);
  assert.throws(() => promoteContainerRelease({ ...options, records: [records[0]] }), /Missing/u);
  assert.throws(() => promoteContainerRelease({ ...options, revision: "b".repeat(40) }), /receipt/u);
  existing = desired.replace(records[0].digest, `sha256:${"f".repeat(64)}`);
  assert.throws(() => promoteContainerRelease(options), /different image set/u);
  assert.equal(mutations.length, 0, "conflicting index must never be overwritten");
  assert.throws(() => promoteContainerRelease({ ...options, docker: () => desired.replace('"amd64"', '"arm64"') }), /platform/u);
}

async function testPublication() {
  const version = "2.3.4";
  const name = `exportdoc-web-${version}-linux-x64.tar.gz`;
  const bytes = "test archive bytes";
  const digest = createHash("sha256").update(bytes).digest("hex");
  await write(`artifacts/releases/${name}`, bytes);
  await write(`artifacts/releases/${name}.sha256`, `${digest}  ${name}\n`);
  const revision = "a".repeat(40);
  let release = null;
  let tagExists = false;
  let tagRevision = revision;
  const mutations = [];
  const assets = [];
  const server = createServer(async (request, response) => {
    const chunks = [];
    for await (const chunk of request) chunks.push(chunk);
    const body = Buffer.concat(chunks);
    const route = request.url.split("?")[0];
    const send = (status, value) => { response.writeHead(status, { "Content-Type": "application/json" }); response.end(JSON.stringify(value)); };
    if (request.method === "GET") {
      if (route.includes("/git/ref/tags/")) return send(tagExists ? 200 : 404, {});
      if (route.includes("/commits/")) return send(200, { sha: tagRevision });
      if (route.includes("/releases/tags/")) return send(release && !release.draft ? 200 : 404, release);
      if (route.endsWith("/releases")) return send(200, release ? [release] : []);
      if (route.endsWith("/assets")) return send(200, assets);
    }
    mutations.push(`${request.method} ${route}`);
    if (route === "/upload") {
      assets.push({ name: new URL(request.url, "http://localhost").searchParams.get("name"), digest: `sha256:${createHash("sha256").update(body).digest("hex")}` });
      return send(201, {});
    }
    if (request.method === "POST" && route.endsWith("/releases")) {
      release = { ...JSON.parse(body), id: 1, upload_url: `http://127.0.0.1:${server.address().port}/upload{?name}`, html_url: "https://example.invalid/release" };
      return send(201, release);
    }
    if (request.method === "PATCH") {
      Object.assign(release, JSON.parse(body));
      tagExists = true;
      return send(200, release);
    }
    return send(500, { unexpected: route });
  });
  await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
  const run = () => promisify(execFile)(process.execPath, [path.join(root, "scripts/github/publish-native-release.mjs")], {
    cwd: temporary, timeout: 15_000, windowsHide: true,
    env: { ...process.env, RELEASE_VERSION: version, GITHUB_REPOSITORY: "owner/repo", GITHUB_SHA: revision,
      GH_TOKEN: "local-test-token", GITHUB_API_URL: `http://127.0.0.1:${server.address().port}` },
  });
  try {
    await run();
    assert.deepEqual(mutations.map(item => item.split(" ")[0]), ["POST", "POST", "POST", "PATCH"]);
    assert.equal(release.draft, false);
    assert.equal(assets.length, 2);
    release.draft = true;
    tagExists = false;
    await run();
    assert.equal(mutations.length, 5, "retry must recover an existing draft without re-uploading or creating another release");
    const before = mutations.length;
    await run();
    assert.equal(mutations.length, before, "identical retries must not replace assets");
    tagRevision = "b".repeat(40);
    await assert.rejects(run(), /different commit/u);
    assert.equal(mutations.length, before, "different commit must not mutate release");
    tagRevision = revision;
    assets[0].digest = `sha256:${"0".repeat(64)}`;
    await assert.rejects(run(), /different or unverifiable bytes/u);
    assert.equal(mutations.length, before, "asset conflict must fail before upload");
    await write(`artifacts/releases/${name}`, "tampered");
    await assert.rejects(run(), /checksum mismatch/u);
    assert.equal(mutations.length, before);
  } finally {
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
  }
}
