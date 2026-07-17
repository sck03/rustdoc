import { spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import zlib from "node:zlib";
import {
  assert,
  assertTemplateSourcePageOrientation,
  locateChromeForTesting,
} from "./lib/report-regression-common.mjs";
import { createReportRegressionTemplateCases } from "./lib/report-regression-template-cases.mjs";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, "..");
const workspaceRoot = path.join(repoRoot, ".codex-runtime", "report-template-pdf-regression");
const pdfRoot = path.join(workspaceRoot, "pdf");

const templateCases = createReportRegressionTemplateCases("pdf");

function renderPdf(chromePath, testCase) {
  const caseRoot = path.join(workspaceRoot, testCase.slug);
  const profilePath = path.join(caseRoot, "ChromeProfile");
  const htmlPath = testCase.relativePath
    ? path.join(repoRoot, ...testCase.relativePath.split("/"))
    : path.join(caseRoot, `${testCase.slug}.html`);
  const pdfPath = path.join(pdfRoot, `${testCase.slug}.pdf`);

  fs.rmSync(caseRoot, { recursive: true, force: true });
  fs.mkdirSync(profilePath, { recursive: true });
  fs.mkdirSync(pdfRoot, { recursive: true });

  if (testCase.html) {
    fs.writeFileSync(htmlPath, testCase.html, "utf8");
  }

  assert(fs.existsSync(htmlPath), `${testCase.slug}: expected HTML source to exist: ${htmlPath}`);
  assertTemplateSourcePageOrientation(testCase, htmlPath);

  const result = spawnSync(
    chromePath,
    [
      "--headless",
      "--disable-gpu",
      "--disable-extensions",
      "--disable-background-networking",
      "--no-first-run",
      `--user-data-dir=${profilePath}`,
      `--print-to-pdf=${pdfPath}`,
      "--print-to-pdf-no-header",
      pathToFileURL(htmlPath).href,
    ],
    {
      encoding: "utf8",
      timeout: 60000,
      windowsHide: true,
    },
  );

  const output = `${result.stdout || ""}\n${result.stderr || ""}`;
  assert(result.status === 0, `${testCase.slug}: Chrome exited with status ${result.status}:\n${output}`);
  assert(fs.existsSync(pdfPath), `${testCase.slug}: PDF was not created.`);

  return { htmlPath, pdfPath };
}

function analyzePdf(pdfPath) {
  const buffer = fs.readFileSync(pdfPath);
  const content = buffer.toString("latin1");
  const mediaBoxes = [...content.matchAll(/\/MediaBox\s*\[\s*([^\]]+?)\s*\]/g)]
    .map((match) => parseMediaBox(match[1]))
    .filter(Boolean);

  assert(content.startsWith("%PDF-"), `${pdfPath}: missing PDF header.`);
  assert(/%%EOF\s*$/.test(content), `${pdfPath}: missing PDF EOF marker.`);
  assert(mediaBoxes.length > 0, `${pdfPath}: missing MediaBox entries.`);

  return {
    bytes: buffer.length,
    pageCount: (content.match(/\/Type\s*\/Page\b/g) || []).length,
    mediaBoxes,
    contentStreams: analyzePdfStreams(content, buffer),
    fontReferenceCount: (content.match(/\/Font\b/g) || []).length,
  };
}

function analyzePdfStreams(content, buffer) {
  const streamMatches = [...content.matchAll(/stream(\r\n|\n|\r)([\s\S]*?)(\r\n|\n|\r)endstream/g)];
  let rawBytes = 0;
  let inflatedBytes = 0;
  let textOperatorCount = 0;
  let drawingOperatorCount = 0;
  let failedInflateCount = 0;

  for (const match of streamMatches) {
    const dictionaryPrefix = getNearestPdfDictionary(content, match.index);
    const declaredLength = parsePdfStreamLength(dictionaryPrefix);
    const dataStart = match.index + "stream".length + match[1].length;
    let streamBytes =
      declaredLength > 0
        ? buffer.subarray(dataStart, dataStart + declaredLength)
        : Buffer.from(match[2], "latin1");
    rawBytes += streamBytes.length;
    if (dictionaryPrefix.includes("/FlateDecode")) {
      try {
        streamBytes = zlib.inflateSync(streamBytes);
        inflatedBytes += streamBytes.length;
      } catch {
        failedInflateCount += 1;
      }
    }

    const streamText = streamBytes.toString("latin1");
    textOperatorCount += countPdfOperators(streamText, ["BT", "ET", "Tj", "TJ"]);
    drawingOperatorCount += countPdfOperators(streamText, ["re", "S", "s", "f", "F", "m", "l", "c", "cm", "Do"]);
  }

  return {
    count: streamMatches.length,
    rawBytes,
    inflatedBytes,
    failedInflateCount,
    textOperatorCount,
    drawingOperatorCount,
  };
}

function getNearestPdfDictionary(content, streamIndex) {
  const dictionaryStart = content.lastIndexOf("<<", streamIndex);
  if (dictionaryStart < 0) {
    return "";
  }

  return content.slice(dictionaryStart, streamIndex);
}

function parsePdfStreamLength(dictionaryText) {
  const match = /\/Length\s+(\d+)/.exec(dictionaryText);
  return match ? Number.parseInt(match[1], 10) : 0;
}

function countPdfOperators(value, operators) {
  return operators.reduce((total, operator) => {
    const escaped = operator.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    return total + (value.match(new RegExp(`(?:^|\\s)${escaped}(?=\\s|$)`, "g")) || []).length;
  }, 0);
}

function parseMediaBox(value) {
  const numbers = value
    .trim()
    .split(/\s+/)
    .map((part) => Number.parseFloat(part))
    .filter((part) => Number.isFinite(part));

  if (numbers.length < 4) {
    return null;
  }

  const [left, bottom, right, top] = numbers;
  return {
    left,
    bottom,
    right,
    top,
    width: roundPdfNumber(Math.abs(right - left)),
    height: roundPdfNumber(Math.abs(top - bottom)),
  };
}

function roundPdfNumber(value) {
  return Math.round(value * 1000) / 1000;
}

function assertPdfMetrics(testCase, metrics) {
  assert(metrics.bytes >= testCase.minBytes, `${testCase.slug}: PDF size ${metrics.bytes} below ${testCase.minBytes}.`);
  assert(
    metrics.pageCount === testCase.expectedPages,
    `${testCase.slug}: expected ${testCase.expectedPages} PDF page(s), found ${metrics.pageCount}.`,
  );

  const firstPage = metrics.mediaBoxes[0];
  const orientation = firstPage.width > firstPage.height ? "landscape" : "portrait";
  assert(
    orientation === testCase.expectedOrientation,
    `${testCase.slug}: expected ${testCase.expectedOrientation} PDF orientation, found ${orientation}.`,
  );
  assert(firstPage.width > 500 && firstPage.height > 500, `${testCase.slug}: PDF MediaBox is unexpectedly small.`);

  for (const [index, mediaBox] of metrics.mediaBoxes.entries()) {
    assert(mediaBox.width > 500 && mediaBox.height > 500, `${testCase.slug}: page ${index + 1} has invalid MediaBox.`);
  }

  assert(
    metrics.fontReferenceCount >= testCase.minFontReferences,
    `${testCase.slug}: expected at least ${testCase.minFontReferences} font reference(s), found ${metrics.fontReferenceCount}.`,
  );
  assert(
    metrics.contentStreams.count >= testCase.expectedPages,
    `${testCase.slug}: expected at least ${testCase.expectedPages} PDF stream(s), found ${metrics.contentStreams.count}.`,
  );
  assert(
    metrics.contentStreams.failedInflateCount === 0,
    `${testCase.slug}: failed to inflate ${metrics.contentStreams.failedInflateCount} PDF stream(s).`,
  );
  assert(
    metrics.contentStreams.inflatedBytes >= testCase.minInflatedStreamBytes,
    `${testCase.slug}: inflated stream bytes ${metrics.contentStreams.inflatedBytes} below ${testCase.minInflatedStreamBytes}.`,
  );
  assert(
    metrics.contentStreams.textOperatorCount >= testCase.minTextOperatorCount,
    `${testCase.slug}: text operator count ${metrics.contentStreams.textOperatorCount} below ${testCase.minTextOperatorCount}.`,
  );
  assert(
    metrics.contentStreams.drawingOperatorCount >= testCase.minDrawingOperatorCount,
    `${testCase.slug}: drawing operator count ${metrics.contentStreams.drawingOperatorCount} below ${testCase.minDrawingOperatorCount}.`,
  );
}

function run() {
  fs.rmSync(workspaceRoot, { recursive: true, force: true });
  fs.mkdirSync(workspaceRoot, { recursive: true });

  const chromePath = locateChromeForTesting(repoRoot);
  const results = [];

  for (const testCase of templateCases) {
    const { htmlPath, pdfPath } = renderPdf(chromePath, testCase);
    const metrics = analyzePdf(pdfPath);
    assertPdfMetrics(testCase, metrics);
    results.push({
      slug: testCase.slug,
      templatePath: testCase.relativePath ?? htmlPath,
      pdfPath,
      expectedPages: testCase.expectedPages,
      expectedOrientation: testCase.expectedOrientation,
      metrics,
    });
  }

  const summaryPath = path.join(workspaceRoot, "pdf-regression-summary.json");
  fs.writeFileSync(summaryPath, `${JSON.stringify({ browserExecutable: chromePath, results }, null, 2)}\n`, "utf8");
  process.stdout.write(`report-template-pdf-regression test passed (${results.length} templates)\n`);
  process.stdout.write(`summary: ${summaryPath}\n`);
}

try {
  run();
} catch (error) {
  process.stderr.write(`${error.stack || error.message}\n`);
  process.exit(1);
}

