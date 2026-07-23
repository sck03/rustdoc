import { createHash } from "node:crypto";
import { existsSync } from "node:fs";
import { mkdir, readFile, rename, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const fontRoot = path.join(repositoryRoot, "Resources", "Fonts", "OpenSource");
const manifestPath = path.join(fontRoot, "font-manifest.json");
const manifest = JSON.parse(await readFile(manifestPath, "utf8"));

if (manifest.schemaVersion !== 1 || !Array.isArray(manifest.fonts) || manifest.fonts.length === 0) {
  throw new Error(`Unsupported or empty report font manifest: ${manifestPath}`);
}

await mkdir(fontRoot, { recursive: true });
for (const font of manifest.fonts) {
  await provisionFont(font);
}

process.stdout.write(`Report fonts ready (${manifest.fonts.length} files): ${fontRoot}\n`);

async function provisionFont(font) {
  const destinationPath = resolveFontPath(font.fileName);
  if (existsSync(destinationPath) && await sha256(destinationPath) === normalizeHash(font.sha256)) {
    process.stdout.write(`  verified ${font.fileName}\n`);
    return;
  }

  const temporaryPath = `${destinationPath}.download`;
  await rm(temporaryPath, { force: true });
  process.stdout.write(`  downloading ${font.fileName}\n`);
  const response = await fetch(font.url, { redirect: "follow" });
  if (!response.ok) {
    throw new Error(`Unable to download ${font.fileName}: HTTP ${response.status} ${response.statusText}`);
  }

  const bytes = Buffer.from(await response.arrayBuffer());
  const actualHash = createHash("sha256").update(bytes).digest("hex").toUpperCase();
  const expectedHash = normalizeHash(font.sha256);
  if (actualHash !== expectedHash) {
    throw new Error(`${font.fileName} SHA-256 mismatch. Expected ${expectedHash}, received ${actualHash}.`);
  }

  await writeFile(temporaryPath, bytes);
  await rm(destinationPath, { force: true });
  await rename(temporaryPath, destinationPath);
}

function resolveFontPath(fileName) {
  if (!fileName || path.basename(fileName) !== fileName) {
    throw new Error(`Invalid font file name in manifest: ${fileName}`);
  }
  return path.join(fontRoot, fileName);
}

async function sha256(filePath) {
  return createHash("sha256").update(await readFile(filePath)).digest("hex").toUpperCase();
}

function normalizeHash(value) {
  const normalized = String(value || "").trim().toUpperCase();
  if (!/^[A-F0-9]{64}$/.test(normalized)) {
    throw new Error(`Invalid SHA-256 in report font manifest: ${value}`);
  }
  return normalized;
}
