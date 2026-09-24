import { normalizeReleaseVersion } from "./release-version.mjs";

export function promoteContainerRelease({ requestedVersion, image, selection, revision, publishLatest, records, docker }) {
  const version = normalizeReleaseVersion(requestedVersion);
  if (!/^ghcr\.io\/[a-z0-9_.-]+\/exportdoc-rust-native$/u.test(image ?? "")) throw new Error("Invalid image name.");
  if (!/^[a-f0-9]{40}$/u.test(revision ?? "")) throw new Error("Invalid source revision.");
  if (!["x64", "arm64", "all"].includes(selection)) throw new Error("Invalid architecture.");
  const architectures = selection === "all" ? ["x64", "arm64"] : [selection];
  if (publishLatest && (selection !== "all" || version.includes("-"))) throw new Error("latest requires all architectures and a stable version.");
  if (records.length !== architectures.length) throw new Error("Missing or extra image receipts.");
  for (const architecture of architectures) {
    const matching = records.filter(record => record.architecture === architecture);
    if (matching.length !== 1) throw new Error(`Missing or duplicate receipt for ${architecture}.`);
    const record = matching[0];
    if (record.version !== version || record.revision !== revision || record.image !== image
        || !/^sha256:[a-f0-9]{64}$/u.test(record.digest)) throw new Error(`Invalid digest receipt for ${architecture}.`);
  }
  const sources = records.map(record => `${image}@${record.digest}`);
  const target = `${image}:${version}`;
  const desired = JSON.parse(docker(["buildx", "imagetools", "create", "--dry-run", ...sources]));
  const expected = { manifests: records.map(record => ({ digest: record.digest,
    platform: { os: "linux", architecture: record.architecture === "x64" ? "amd64" : "arm64" } })) };
  if (canonicalIndex(desired) !== canonicalIndex(expected)) throw new Error("Image digest/platform does not match validated receipts.");
  const existing = docker(["buildx", "imagetools", "inspect", "--raw", target], true);
  if (existing && canonicalIndex(JSON.parse(existing)) !== canonicalIndex(desired)) {
    throw new Error(`${target} already contains a different image set. Choose a new version.`);
  }
  if (!existing) docker(["buildx", "imagetools", "create", "--tag", target, ...sources]);
  if (canonicalIndex(JSON.parse(docker(["buildx", "imagetools", "inspect", "--raw", target]))) !== canonicalIndex(desired)) throw new Error("Published index does not match validated images.");
  if (publishLatest) docker(["buildx", "imagetools", "create", "--tag", `${image}:latest`, target]);
  return { version, revision, reference: target, images: records };
}

function canonicalIndex(index) {
  if (!Array.isArray(index.manifests)) throw new Error("Expected an OCI/Docker image index.");
  return JSON.stringify(index.manifests.map(item => ({ digest: item.digest, os: item.platform?.os,
    architecture: item.platform?.architecture }))
    .sort((a, b) => a.digest.localeCompare(b.digest)));
}
