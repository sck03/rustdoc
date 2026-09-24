import { createReadStream } from "node:fs";
import { readFile, readdir, stat } from "node:fs/promises";
import { createHash } from "node:crypto";
import path from "node:path";
import { normalizeReleaseVersion } from "../lib/release-version.mjs";

const version = normalizeReleaseVersion(process.env.RELEASE_VERSION);
const repository = process.env.GITHUB_REPOSITORY;
const revision = process.env.GITHUB_SHA;
if (!/^[\w.-]+\/[\w.-]+$/u.test(repository ?? "") || !/^[a-f0-9]{40}$/u.test(revision ?? "")) throw new Error("Invalid release repository/revision.");
if (!process.env.GH_TOKEN) throw new Error("GH_TOKEN is required for release publication.");
const api = `${process.env.GITHUB_API_URL || "https://api.github.com"}/repos/${repository}`;
const headers = { Authorization: `Bearer ${process.env.GH_TOKEN}`, Accept: "application/vnd.github+json", "Content-Type": "application/json", "X-GitHub-Api-Version": "2022-11-28" };
const directory = path.resolve("artifacts/releases");
const files = (await readdir(directory)).sort();
if (!files.length) throw new Error("No release archives found.");
const digests = new Map();
for (const name of files) {
  if (!name.startsWith("exportdoc-") || !name.includes(`-${version}-`) || !/\.(?:zip|tar\.gz)(?:\.sha256)?$/u.test(name)) throw new Error(`Unexpected release asset: ${name}`);
  if (name.endsWith(".sha256")) {
    if (!files.includes(name.slice(0, -7))) throw new Error(`Orphan checksum: ${name}`);
    continue;
  }
  const hash = createHash("sha256");
  for await (const chunk of createReadStream(path.join(directory, name))) hash.update(chunk);
  const digest = hash.digest("hex");
  const expected = (await readFile(path.join(directory, `${name}.sha256`), "utf8")).trim();
  if (expected !== `${digest}  ${name}`) throw new Error(`Archive checksum mismatch: ${name}`);
  digests.set(name, digest);
}
const tag = `v${version}`;
const tagRef = await request(`/git/ref/tags/${encodeURIComponent(tag)}`, { missing: true });
const commit = tagRef ? await request(`/commits/${encodeURIComponent(tag)}`) : null;
if (commit && commit.sha !== revision) throw new Error(`${tag} already belongs to a different commit; choose a new version.`);
let release = await request(`/releases/tags/${encodeURIComponent(tag)}`, { missing: true });
if (!release) {
  // A failed initial upload can leave a draft whose Git tag does not exist yet.
  for (let page = 1; ; page++) {
    const batch = await request(`/releases?per_page=100&page=${page}`);
    const matches = batch.filter(item => item.draft && item.tag_name === tag);
    if (matches.length > 1 || (release && matches.length)) throw new Error(`Multiple drafts exist for ${tag}; resolve the ambiguity before publishing.`);
    release = matches[0] || release;
    if (batch.length < 100) break;
  }
}
if (release && !commit) {
  const targetCommit = await request(`/commits/${encodeURIComponent(release.target_commitish)}`);
  if (targetCommit.sha !== revision) throw new Error(`Existing draft ${tag} belongs to a different commit.`);
}
if (!release) {
  release = await request("/releases", { method: "POST", body: {
    tag_name: tag, target_commitish: revision, name: `ExportDocManager ${version}`,
    draft: true, prerelease: version.includes("-"),
    body: `Rust + React Full packages for ${revision}. SHA-256 checksums accompany each archive. Desktop installer updates require separately configured Tauri updater trust.`,
  } });
}
// Refuse an existing different asset; retries may reuse byte-identical assets.
const assets = [];
for (let page = 1; ; page++) {
  const batch = await request(`/releases/${release.id}/assets?per_page=100&page=${page}`);
  assets.push(...batch);
  if (batch.length < 100) break;
}
for (const name of files) {
  const file = path.join(directory, name);
  const digest = digests.get(name) || createHash("sha256").update(await readFile(file)).digest("hex");
  const existing = assets.find(asset => asset.name === name);
  if (existing && existing.digest !== `sha256:${digest}`) throw new Error(`Release asset already exists with different or unverifiable bytes: ${name}`);
}
for (const name of files) {
  if (assets.some(asset => asset.name === name)) continue;
  const file = path.join(directory, name);
  const url = `${release.upload_url.split("{")[0]}?name=${encodeURIComponent(name)}`;
  const response = await fetch(url, { method: "POST", headers: { ...headers, "Content-Type": "application/octet-stream", "Content-Length": String((await stat(file)).size) },
    body: createReadStream(file), duplex: "half", signal: AbortSignal.timeout(600_000) });
  if (!response.ok) throw new Error(`Asset upload failed (${response.status}): ${name}`);
}
if (release.draft) release = await request(`/releases/${release.id}`, { method: "PATCH", body: { draft: false, make_latest: "false" } });
console.log(`Published ${release.html_url}`);

async function request(route, { method = "GET", body, missing = false } = {}) {
  const response = await fetch(api + route, { method, headers, body: body ? JSON.stringify(body) : undefined, signal: AbortSignal.timeout(30_000) });
  if (missing && response.status === 404) return null;
  if (!response.ok) throw new Error(`GitHub release API failed (${response.status}) for ${route}`);
  return response.json();
}
