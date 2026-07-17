import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import {
  analyzeReportScreenshot,
  assert,
  assertBoundsWithinTolerance,
  assertTemplateSourcePageOrientation,
  hexHammingDistance,
  locateChromeForTesting,
  pickBounds,
} from "./lib/report-regression-common.mjs";
import { createReportRegressionTemplateCases } from "./lib/report-regression-template-cases.mjs";
import {
  CdpClient,
  closeChrome,
  delay,
  getPageWebSocketUrl,
  waitForDevToolsUrl,
} from "./lib/chromium-cdp.mjs";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, "..");
const workspaceRoot = path.join(repoRoot, ".codex-runtime", "report-template-print-pixel-regression");
const screenshotRoot = path.join(workspaceRoot, "screenshots");
const baselinePath = path.join(repoRoot, "tests", "ReportTemplateFixtures", "report_template_print_pixel_baselines.json");
const fingerprintGrid = {
  columns: 32,
  rows: 32,
  minNonWhiteRatio: 0.006,
};
const baselineDefaults = {
  maxFingerprintDistance: 42,
  maxContentBoundsDelta: 24,
  maxFullPageSizeDelta: 24,
};

const templateCases = createReportRegressionTemplateCases("printPixel");

function resolveHtmlPath(testCase) {
  if (testCase.relativePath) {
    const absoluteTemplatePath = path.join(repoRoot, ...testCase.relativePath.split("/"));
    assert(fs.existsSync(absoluteTemplatePath), `Expected template file to exist: ${absoluteTemplatePath}`);
    assertTemplateSourcePageOrientation(testCase, absoluteTemplatePath);
    return absoluteTemplatePath;
  }

  const caseRoot = path.join(workspaceRoot, testCase.slug);
  fs.mkdirSync(caseRoot, { recursive: true });
  const htmlPath = path.join(caseRoot, `${testCase.slug}.html`);
  fs.writeFileSync(htmlPath, testCase.html, "utf8");
  return htmlPath;
}

async function renderPrintScreenshot(chromePath, testCase) {
  const htmlPath = resolveHtmlPath(testCase);
  const profilePath = path.join(workspaceRoot, `ChromeProfile-${testCase.slug}`);
  const screenshotPath = path.join(screenshotRoot, `${testCase.slug}.png`);
  const fullPageScreenshotPath = path.join(screenshotRoot, `${testCase.slug}.full.png`);
  fs.rmSync(profilePath, { recursive: true, force: true });
  fs.mkdirSync(profilePath, { recursive: true });
  fs.mkdirSync(screenshotRoot, { recursive: true });

  const child = spawn(
    chromePath,
    [
      "--headless",
      "--disable-gpu",
      "--disable-extensions",
      "--disable-background-networking",
      "--no-first-run",
      "--hide-scrollbars",
      "--force-device-scale-factor=1",
      "--font-render-hinting=none",
      "--remote-debugging-port=0",
      `--user-data-dir=${profilePath}`,
      `--window-size=${testCase.viewport.width},${testCase.viewport.height}`,
      "about:blank",
    ],
    {
      stdio: ["ignore", "pipe", "pipe"],
      windowsHide: true,
    },
  );

  let browserWebSocketUrl;
  try {
    browserWebSocketUrl = await waitForDevToolsUrl(child, testCase.slug);
    const pageWebSocketUrl = await getPageWebSocketUrl(browserWebSocketUrl, testCase.slug);
    const page = await CdpClient.connect(pageWebSocketUrl);
    try {
      await page.send("Page.enable");
      await page.send("Runtime.enable");
      await page.send("Emulation.setDeviceMetricsOverride", {
        width: testCase.viewport.width,
        height: testCase.viewport.height,
        deviceScaleFactor: 1,
        mobile: false,
      });
      await page.send("Emulation.setEmulatedMedia", { media: "print" });
      const loadEvent = page.waitForEvent("Page.loadEventFired", () => true, 30000);
      await page.send("Page.navigate", { url: pathToFileURL(htmlPath).href });
      await loadEvent;
      await page.send("Runtime.evaluate", {
        expression:
          "document.fonts && document.fonts.ready ? document.fonts.ready.then(() => true) : true",
        awaitPromise: true,
        returnByValue: true,
      });
      await page.send("Runtime.evaluate", {
        expression: "new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))",
        awaitPromise: true,
        returnByValue: true,
      });

      const screenshot = await page.send("Page.captureScreenshot", {
        format: "png",
        fromSurface: true,
        captureBeyondViewport: false,
      });
      assert(screenshot.data, `${testCase.slug}: Chrome did not return screenshot data.`);
      fs.writeFileSync(screenshotPath, Buffer.from(screenshot.data, "base64"));

      const documentSize = await readDocumentSize(page);
      const fullPageScreenshot = await page.send("Page.captureScreenshot", {
        format: "png",
        fromSurface: true,
        captureBeyondViewport: true,
        clip: {
          x: 0,
          y: 0,
          width: documentSize.width,
          height: documentSize.height,
          scale: 1,
        },
      });
      assert(fullPageScreenshot.data, `${testCase.slug}: Chrome did not return full-page screenshot data.`);
      fs.writeFileSync(fullPageScreenshotPath, Buffer.from(fullPageScreenshot.data, "base64"));
    } finally {
      page.close();
    }
  } finally {
    await closeChrome(browserWebSocketUrl, child);
  }

  assert(fs.existsSync(screenshotPath), `${testCase.slug}: screenshot was not created.`);
  assert(fs.existsSync(fullPageScreenshotPath), `${testCase.slug}: full-page screenshot was not created.`);
  return { htmlPath, screenshotPath, fullPageScreenshotPath };
}

async function readDocumentSize(page) {
  const response = await page.send("Runtime.evaluate", {
    expression: `(() => {
      const body = document.body || {};
      const documentElement = document.documentElement || {};
      const width = Math.ceil(Math.max(
        window.innerWidth || 0,
        body.scrollWidth || 0,
        body.offsetWidth || 0,
        documentElement.clientWidth || 0,
        documentElement.scrollWidth || 0,
        documentElement.offsetWidth || 0
      ));
      const height = Math.ceil(Math.max(
        window.innerHeight || 0,
        body.scrollHeight || 0,
        body.offsetHeight || 0,
        documentElement.clientHeight || 0,
        documentElement.scrollHeight || 0,
        documentElement.offsetHeight || 0
      ));
      return { width, height };
    })()`,
    returnByValue: true,
  });
  const value = response?.result?.value;
  assert(value && value.width > 0 && value.height > 0, "Unable to read document size for full-page screenshot.");
  return {
    width: Math.min(Math.ceil(value.width), 4096),
    height: Math.min(Math.ceil(value.height), 12000),
  };
}

function assertPrintMetrics(testCase, metrics) {
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

function assertFullPageMetrics(testCase, viewportMetrics, fullPageMetrics) {
  assert(
    fullPageMetrics.width >= testCase.viewport.width,
    `${testCase.slug}: full-page screenshot width ${fullPageMetrics.width} below viewport width ${testCase.viewport.width}.`,
  );
  const maxFullPageWidthRatio = testCase.maxFullPageWidthRatio ?? 1.02;
  const maxFullPageWidth = Math.ceil(testCase.viewport.width * maxFullPageWidthRatio);
  assert(
    fullPageMetrics.width <= maxFullPageWidth,
    `${testCase.slug}: full-page screenshot width ${fullPageMetrics.width} exceeds ${maxFullPageWidth} (${maxFullPageWidthRatio}x viewport).`,
  );
  assert(
    fullPageMetrics.height >= testCase.viewport.height,
    `${testCase.slug}: full-page screenshot height ${fullPageMetrics.height} below viewport height ${testCase.viewport.height}.`,
  );
  if (testCase.minFullPageHeight) {
    assert(
      fullPageMetrics.height >= testCase.minFullPageHeight,
      `${testCase.slug}: full-page screenshot height ${fullPageMetrics.height} below ${testCase.minFullPageHeight}.`,
    );
  }

  assert(
    fullPageMetrics.nonWhitePixels >= viewportMetrics.nonWhitePixels,
    `${testCase.slug}: full-page non-white pixels ${fullPageMetrics.nonWhitePixels} below viewport ${viewportMetrics.nonWhitePixels}.`,
  );
  assert(fullPageMetrics.contentBounds, `${testCase.slug}: expected full-page non-white content bounds.`);
  for (const sample of fullPageMetrics.colorSamples) {
    assert(
      sample.pixels >= sample.minPixels,
      `${testCase.slug}: expected full-page at least ${sample.minPixels} pixels near ${sample.color}, found ${sample.pixels}.`,
    );
  }
}

function loadPrintPixelBaselines() {
  assert(fs.existsSync(baselinePath), `Print pixel baseline file is missing: ${baselinePath}`);
  const baseline = JSON.parse(fs.readFileSync(baselinePath, "utf8"));
  assert(baseline && baseline.version === 1, "Print pixel baseline file has an unsupported version.");
  assert(baseline.pages && typeof baseline.pages === "object", "Print pixel baseline file is missing pages.");
  assert(
    baseline.grid?.columns === fingerprintGrid.columns &&
      baseline.grid?.rows === fingerprintGrid.rows &&
      baseline.grid?.minNonWhiteRatio === fingerprintGrid.minNonWhiteRatio,
    "Print pixel baseline grid does not match the current fingerprint settings.",
  );
  return baseline;
}

function buildPrintPixelBaseline(results) {
  const pages = {};
  for (const result of results) {
    pages[result.slug] = {
      viewport: summarizePrintMetricsForBaseline(result.metrics),
      fullPage: summarizePrintMetricsForBaseline(result.fullPageMetrics),
    };
  }

  return {
    version: 1,
    description:
      "Golden layout fingerprints for print-media screenshots generated from legacy/customer and built-in report templates.",
    grid: fingerprintGrid,
    defaults: baselineDefaults,
    pages,
  };
}

function summarizePrintMetricsForBaseline(metrics) {
  return {
    width: metrics.width,
    height: metrics.height,
    fingerprintHex: metrics.layoutFingerprint.hex,
    occupiedCells: metrics.layoutFingerprint.occupiedCells,
    contentBounds: pickBounds(metrics.contentBounds),
  };
}

function assertPrintPixelBaselines(results, baseline) {
  const actualKeys = new Set(results.map((result) => result.slug));
  const baselineKeys = new Set(Object.keys(baseline.pages));
  for (const baselineKey of baselineKeys) {
    assert(actualKeys.has(baselineKey), `Print pixel baseline has no rendered sample in this run: ${baselineKey}`);
  }

  for (const result of results) {
    const expected = baseline.pages[result.slug];
    assert(expected, `Print pixel baseline is missing rendered sample: ${result.slug}`);
    const options = {
      ...baselineDefaults,
      ...(baseline.defaults ?? {}),
      ...(expected.tolerance ?? {}),
    };
    assertPrintBaselineEntry(`${result.slug} viewport`, result.metrics, expected.viewport, options, false);
    assertPrintBaselineEntry(`${result.slug} full-page`, result.fullPageMetrics, expected.fullPage, options, true);
  }
}

function assertPrintBaselineEntry(label, actual, expected, options, fullPage) {
  assert(expected, `${label}: missing baseline entry.`);
  assert(actual.width === expected.width, `${label}: width ${actual.width} differs from baseline ${expected.width}.`);
  const heightDelta = Math.abs(actual.height - expected.height);
  const allowedHeightDelta = fullPage ? options.maxFullPageSizeDelta : 0;
  assert(heightDelta <= allowedHeightDelta, `${label}: height changed by ${heightDelta}px, allowed ${allowedHeightDelta}px.`);

  const distance = hexHammingDistance(actual.layoutFingerprint.hex, expected.fingerprintHex);
  assert(
    distance <= options.maxFingerprintDistance,
    `${label}: print layout fingerprint distance ${distance} exceeds ${options.maxFingerprintDistance}.`,
  );

  assertBoundsWithinTolerance(
    `${label} content`,
    actual.contentBounds,
    expected.contentBounds,
    options.maxContentBoundsDelta,
  );
}

async function run() {
  fs.rmSync(workspaceRoot, { recursive: true, force: true });
  fs.mkdirSync(workspaceRoot, { recursive: true });
  const chromePath = locateChromeForTesting(repoRoot);
  const results = [];

  for (const testCase of templateCases) {
    const { htmlPath, screenshotPath, fullPageScreenshotPath } = await renderPrintScreenshot(chromePath, testCase);
    const metrics = analyzeReportScreenshot(screenshotPath, testCase.expectedColorSamples, {
      fingerprintGrid,
    });
    const fullPageMetrics = analyzeReportScreenshot(
      fullPageScreenshotPath,
      testCase.expectedFullPageColorSamples || testCase.expectedColorSamples,
      { fingerprintGrid },
    );
    assertPrintMetrics(testCase, metrics);
    assertFullPageMetrics(testCase, metrics, fullPageMetrics);
    results.push({
      slug: testCase.slug,
      templatePath: testCase.relativePath || htmlPath,
      screenshotPath,
      fullPageScreenshotPath,
      metrics,
      fullPageMetrics,
    });
  }

  const actualBaseline = buildPrintPixelBaseline(results);
  const actualBaselinePath = path.join(workspaceRoot, "print-pixel-baseline.actual.json");
  fs.writeFileSync(actualBaselinePath, `${JSON.stringify(actualBaseline, null, 2)}\n`, "utf8");
  assertPrintPixelBaselines(results, loadPrintPixelBaselines());

  const summaryPath = path.join(workspaceRoot, "print-pixel-regression-summary.json");
  fs.writeFileSync(summaryPath, `${JSON.stringify({ browserExecutable: chromePath, results }, null, 2)}\n`, "utf8");
  process.stdout.write(`report-template-print-pixel-regression test passed (${results.length} templates)\n`);
  process.stdout.write(`summary: ${summaryPath}\n`);
  process.stdout.write(`actual baseline: ${actualBaselinePath}\n`);
}

try {
  await run();
} catch (error) {
  process.stderr.write(`${error.stack || error.message}\n`);
  process.exit(1);
}

