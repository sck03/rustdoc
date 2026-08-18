import { existsSync, readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const sourceRoot = path.join(repositoryRoot, "apps", "export-doc-web", "src");
const baselinePath = path.join(repositoryRoot, "scripts", "baselines", "frontend-style-governance.json");
const colorTokenPath = path.join(sourceRoot, "styles", "theme", "color-tokens.css");
const cssFiles = walk(sourceRoot).filter((file) => file.endsWith(".css"));
const cssSources = cssFiles.map((file) => ({ file, source: readFileSync(file, "utf8") }));
const css = cssSources
  .map(({ source }) => source)
  .join("\n")
  .replaceAll(/\/\*[\s\S]*?\*\//gu, "");
const featureCss = cssSources
  .filter(({ file }) => path.resolve(file) !== path.resolve(colorTokenPath))
  .map(({ source }) => source)
  .join("\n")
  .replaceAll(/\/\*[\s\S]*?\*\//gu, "");

const current = {
  cssFiles: cssFiles.length,
  sourceLines: css.split(/\r?\n/u).length,
  hexColors: count(/(?:^|[^\w-])#[0-9a-f]{3,8}\b/giu),
  rgbColors: count(/\brgba?\s*\(/giu),
  featureHexColors: countIn(featureCss, /(?:^|[^\w-])#[0-9a-f]{3,8}\b/giu),
  featureRgbColors: countIn(featureCss, /\brgba?\s*\(/giu),
  boxShadows: count(/\bbox-shadow\s*:/giu),
  gradients: count(/\b(?:linear|radial|conic)-gradient\s*\(/giu),
  pixelFontSizes: count(/\bfont-size\s*:\s*[^;{}]*?\d+(?:\.\d+)?px\b/giu),
  importantDeclarations: count(/!important\b/giu),
};

if (process.argv.includes("--print-current")) {
  process.stdout.write(`${JSON.stringify(current, null, 2)}\n`);
  process.exit(0);
}

if (!existsSync(baselinePath)) {
  throw new Error(`Style governance baseline was not found: ${baselinePath}`);
}

const baseline = JSON.parse(readFileSync(baselinePath, "utf8"));
const failures = [];
for (const metric of [
  "hexColors",
  "rgbColors",
  "featureHexColors",
  "featureRgbColors",
  "boxShadows",
  "gradients",
  "pixelFontSizes",
  "importantDeclarations",
]) {
  const maximum = Number(baseline.maximum?.[metric]);
  if (!Number.isFinite(maximum)) {
    failures.push(`Baseline is missing maximum.${metric}.`);
  } else if (current[metric] > maximum) {
    failures.push(`${metric} increased from the governed maximum ${maximum} to ${current[metric]}.`);
  }
}

if (failures.length > 0) {
  process.stderr.write("Frontend style governance failed. Prefer semantic design tokens and shared components before intentionally reviewing the baseline.\n");
  process.stderr.write(`${failures.join("\n")}\n`);
  process.stderr.write(`Current metrics: ${JSON.stringify(current)}\n`);
  process.exit(1);
}

process.stdout.write(`Frontend style governance passed: ${JSON.stringify(current)}\n`);

function count(pattern) {
  return [...css.matchAll(pattern)].length;
}

function countIn(source, pattern) {
  return [...source.matchAll(pattern)].length;
}

function walk(directory) {
  const files = [];
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) files.push(...walk(fullPath));
    else files.push(fullPath);
  }
  return files;
}
