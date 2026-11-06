import { existsSync } from "node:fs";
import { mkdir, readFile, writeFile, rename, rm } from "node:fs/promises";
import { createHash, randomUUID } from "node:crypto";
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { downloadVerifiedFile, fileSha256 } from "./lib/verified-download.mjs";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const fontRoot = path.join(repositoryRoot, "Resources", "Fonts", "OpenSource");
const manifestPath = path.join(fontRoot, "font-manifest.json");
const manifest = JSON.parse(await readFile(manifestPath, "utf8"));
const web = process.argv.includes("--web");
if (process.argv.slice(2).some(value => value !== "--web")) throw new Error("Expected only --web.");

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
  if (web) for (const font of manifest.fonts) if (font.web) await provisionWebFont(font);
  process.stdout.write(`Report fonts ready (${manifest.fonts.length} files): ${fontRoot}\n`);
} finally {
  process.removeListener("SIGINT", cancel);
  process.removeListener("SIGTERM", cancel);
}

async function provisionWebFont(font) {
  const destination = resolveFontPath(font.web.fileName);
  const expected = normalizeHash(font.web.sha256);
  if (existsSync(destination) && await fileSha256(destination, { signal: cancellation.signal }) === expected) return;
  const require = createRequire(path.join(repositoryRoot, "apps/export-doc-web/package.json"));
  const { compress } = require("wawoff2");
  cancellation.signal.throwIfAborted();
  const bytes = Buffer.from(await compress(await readFile(resolveFontPath(font.fileName))));
  cancellation.signal.throwIfAborted();
  if (createHash("sha256").update(bytes).digest("hex").toUpperCase() !== expected) {
    throw new Error(`WOFF2 SHA-256 mismatch: ${font.web.fileName}`);
  }
  const temporary = `${destination}.${randomUUID()}.tmp`;
  try {
    await writeFile(temporary, bytes, { flag: "wx" });
    await rename(temporary, destination);
  } finally {
    await rm(temporary, { force: true });
  }
  process.stdout.write(`  compressed ${font.web.fileName} (${bytes.length} bytes)\n`);
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
