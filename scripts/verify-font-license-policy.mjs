import { createHash } from "node:crypto";
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const argumentsMap = readArguments(process.argv.slice(2));
const scanRoot = path.resolve(argumentsMap.root || repositoryRoot);
const requireFiles = argumentsMap.requireFiles === true;
const sourceFontRoot = path.join(repositoryRoot, "Resources", "Fonts", "OpenSource");
const manifest = JSON.parse(readFileSync(path.join(sourceFontRoot, "font-manifest.json"), "utf8"));
const expected = new Map(manifest.fonts.map((font) => [font.fileName.toLowerCase(), font]));
const fontExtensions = new Set([".ttf", ".otf", ".ttc", ".woff", ".woff2", ".eot"]);
const ignoredDirectoryNames = new Set([".git", ".codex-runtime", "artifacts", "bin", "obj", "node_modules", "target", "dist"]);
const forbiddenNamePattern = /(msyh|microsoft\s*yahei|simsun|simhei|segoe|arial|times\s*new\s*roman|sf\s*pro|pingfang|hiragino|consolas)/i;
const discovered = [];

walk(scanRoot);

for (const file of discovered) {
  if (forbiddenNamePattern.test(path.basename(file))) {
    throw new Error(`Proprietary or unapproved font binary detected: ${file}`);
  }

  const manifestEntry = expected.get(path.basename(file).toLowerCase());
  const normalized = file.split(path.sep).join("/").toLowerCase();
  if (!manifestEntry || !normalized.includes("/resources/fonts/opensource/")) {
    throw new Error(`Font binary is outside the approved open-source font manifest: ${file}`);
  }

  const actualHash = createHash("sha256").update(readFileSync(file)).digest("hex").toUpperCase();
  if (actualHash !== manifestEntry.sha256.toUpperCase()) {
    throw new Error(`${file} does not match the approved SHA-256 in font-manifest.json.`);
  }
}

if (requireFiles) {
  for (const font of manifest.fonts) {
    const matching = discovered.find((file) => path.basename(file).toLowerCase() === font.fileName.toLowerCase());
    if (!matching) {
      throw new Error(`Required open-source report font is missing from ${scanRoot}: ${font.fileName}`);
    }
  }
}

const licenseFile = path.join(sourceFontRoot, manifest.licenseFile);
if (!existsSync(licenseFile) || statSync(licenseFile).size < 1000) {
  throw new Error(`Font license notice is missing or incomplete: ${licenseFile}`);
}
const licenseHash = createHash("sha256").update(readFileSync(licenseFile)).digest("hex").toUpperCase();
if (licenseHash !== String(manifest.licenseSha256 || "").toUpperCase()) {
  throw new Error(`Font license notice does not match the pinned upstream text: ${licenseFile}`);
}

process.stdout.write(`Font license policy verified: root=${scanRoot}, binaries=${discovered.length}, requireFiles=${requireFiles}\n`);

function walk(directory) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    if (entry.isDirectory() && ignoredDirectoryNames.has(entry.name)) continue;
    const absolutePath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      walk(absolutePath);
    } else if (entry.isFile() && fontExtensions.has(path.extname(entry.name).toLowerCase())) {
      discovered.push(absolutePath);
    }
  }
}

function readArguments(values) {
  const result = {};
  for (let index = 0; index < values.length; index += 1) {
    const value = values[index];
    if (value === "--require-files") {
      result.requireFiles = true;
    } else if (value === "--root") {
      result.root = values[index + 1];
      index += 1;
    } else {
      throw new Error(`Unknown argument: ${value}`);
    }
  }
  return result;
}
