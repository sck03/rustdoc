import { existsSync } from "node:fs";
import { mkdir, readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { downloadVerifiedFile, fileSha256 } from "./lib/verified-download.mjs";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const fontRoot = path.join(repositoryRoot, "Resources", "Fonts", "OpenSource");
const manifestPath = path.join(fontRoot, "font-manifest.json");
const manifest = JSON.parse(await readFile(manifestPath, "utf8"));

if (manifest.schemaVersion !== 1 || !Array.isArray(manifest.fonts) || manifest.fonts.length === 0) {
  throw new Error(`Unsupported or empty report font manifest: ${manifestPath}`);
}

const cancellation = new AbortController();
const cancel = () => cancellation.abort(new Error("Report font provisioning cancelled."));
process.once("SIGINT", cancel);
process.once("SIGTERM", cancel);
try {
  await mkdir(fontRoot, { recursive: true });
  for (const font of manifest.fonts) await provisionFont(font);
  process.stdout.write(`Report fonts ready (${manifest.fonts.length} files): ${fontRoot}\n`);
} finally {
  process.removeListener("SIGINT", cancel);
  process.removeListener("SIGTERM", cancel);
}

async function provisionFont(font) {
  const destinationPath = resolveFontPath(font.fileName);
  cancellation.signal.throwIfAborted();
  const expectedHash = normalizeHash(font.sha256);
  if (existsSync(destinationPath) && await fileSha256(destinationPath, { signal: cancellation.signal }) === expectedHash) {
    process.stdout.write(`  verified ${font.fileName}\n`);
    return;
  }

  process.stdout.write(`  downloading ${font.fileName}\n`);
  await downloadVerifiedFile(font.url, destinationPath, { sha256: expectedHash, maxBytes: 32 * 1024 * 1024, signal: cancellation.signal });
}

function resolveFontPath(fileName) {
  if (!fileName || path.basename(fileName) !== fileName) {
    throw new Error(`Invalid font file name in manifest: ${fileName}`);
  }
  return path.join(fontRoot, fileName);
}

function normalizeHash(value) {
  const normalized = String(value || "").trim().toUpperCase();
  if (!/^[A-F0-9]{64}$/.test(normalized)) {
    throw new Error(`Invalid SHA-256 in report font manifest: ${value}`);
  }
  return normalized;
}
