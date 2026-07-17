import { spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import {
  analyzeReportScreenshot,
  assert,
  assertTemplateSourcePageOrientation,
  locateChromeForTesting,
} from "./lib/report-regression-common.mjs";
import { createReportRegressionTemplateCases } from "./lib/report-regression-template-cases.mjs";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, "..");
const workspaceRoot = path.join(repoRoot, ".codex-runtime", "report-template-visual-regression");
const screenshotRoot = path.join(workspaceRoot, "screenshots");

const templateCases = createReportRegressionTemplateCases("visual");

function assertVisualMetrics(testCase, metrics) {
  assert(metrics.width === testCase.viewport.width, `${testCase.slug}: screenshot width mismatch.`);
  assert(metrics.height === testCase.viewport.height, `${testCase.slug}: screenshot height mismatch.`);
  assert(
    metrics.nonWhiteRatio >= testCase.minNonWhiteRatio,
    `${testCase.slug}: non-white pixel ratio ${metrics.nonWhiteRatio} below ${testCase.minNonWhiteRatio}.`,
  );
  assert(
    metrics.darkRatio >= testCase.minDarkRatio,
    `${testCase.slug}: dark pixel ratio ${metrics.darkRatio} below ${testCase.minDarkRatio}.`,
  );
  assert(metrics.contentBounds, `${testCase.slug}: expected non-white content bounds.`);
  assert(
    metrics.contentBounds.width >= testCase.viewport.width * testCase.minContentWidthRatio,
    `${testCase.slug}: content width ${metrics.contentBounds.width} below expected ratio ${testCase.minContentWidthRatio}.`,
  );
  assert(
    metrics.contentBounds.height >= testCase.minContentHeight,
    `${testCase.slug}: content height ${metrics.contentBounds.height} below ${testCase.minContentHeight}.`,
  );
  assert(
    metrics.colorBucketCount >= testCase.minColorBuckets,
    `${testCase.slug}: color bucket count ${metrics.colorBucketCount} below ${testCase.minColorBuckets}.`,
  );

  for (const sample of metrics.colorSamples) {
    assert(
      sample.pixels >= sample.minPixels,
      `${testCase.slug}: expected at least ${sample.minPixels} pixels near ${sample.color}, found ${sample.pixels}.`,
    );
  }
}

function renderScreenshot(chromePath, testCase) {
  const absoluteTemplatePath = path.join(repoRoot, ...testCase.relativePath.split("/"));
  assert(fs.existsSync(absoluteTemplatePath), `Expected template file to exist: ${absoluteTemplatePath}`);
  assertTemplateSourcePageOrientation(testCase, absoluteTemplatePath);

  const profilePath = path.join(workspaceRoot, `ChromeProfile-${testCase.slug}`);
  const screenshotPath = path.join(screenshotRoot, `${testCase.slug}.png`);
  fs.rmSync(profilePath, { recursive: true, force: true });
  fs.mkdirSync(profilePath, { recursive: true });
  fs.mkdirSync(screenshotRoot, { recursive: true });

  const result = spawnSync(
    chromePath,
    [
      "--headless",
      "--disable-gpu",
      "--disable-extensions",
      "--no-first-run",
      "--hide-scrollbars",
      "--force-device-scale-factor=1",
      "--font-render-hinting=none",
      `--user-data-dir=${profilePath}`,
      `--window-size=${testCase.viewport.width},${testCase.viewport.height}`,
      `--screenshot=${screenshotPath}`,
      pathToFileURL(absoluteTemplatePath).href,
    ],
    {
      encoding: "utf8",
      timeout: 60000,
      windowsHide: true,
    },
  );

  const output = `${result.stdout || ""}\n${result.stderr || ""}`;
  assert(result.status === 0, `${testCase.slug}: Chrome exited with status ${result.status}:\n${output}`);
  assert(fs.existsSync(screenshotPath), `${testCase.slug}: screenshot was not created.`);

  return screenshotPath;
}

function run() {
  fs.rmSync(workspaceRoot, { recursive: true, force: true });
  fs.mkdirSync(workspaceRoot, { recursive: true });
  const chromePath = locateChromeForTesting(repoRoot);
  const results = [];

  for (const testCase of templateCases) {
    const screenshotPath = renderScreenshot(chromePath, testCase);
    const metrics = analyzeReportScreenshot(screenshotPath, testCase.expectedColorSamples);
    assertVisualMetrics(testCase, metrics);
    results.push({
      slug: testCase.slug,
      templatePath: testCase.relativePath,
      screenshotPath,
      metrics,
    });
  }

  const summaryPath = path.join(workspaceRoot, "visual-regression-summary.json");
  fs.writeFileSync(summaryPath, `${JSON.stringify({ browserExecutable: chromePath, results }, null, 2)}\n`, "utf8");
  process.stdout.write(`report-template-visual-regression test passed (${results.length} templates)\n`);
  process.stdout.write(`summary: ${summaryPath}\n`);
}

try {
  run();
} catch (error) {
  process.stderr.write(`${error.stack || error.message}\n`);
  process.exit(1);
}

