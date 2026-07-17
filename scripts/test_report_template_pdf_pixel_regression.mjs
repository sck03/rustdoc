import { spawn, spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import {
  assert,
  assertBoundsWithinTolerance,
  assertTemplateSourcePageOrientation,
  bitsToHex,
  hexHammingDistance,
  locateChromeForTesting,
  parsePng,
  pickBounds,
  roundRatio,
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
const workspaceRoot = path.join(repoRoot, ".codex-runtime", "report-template-pdf-pixel-regression");
const pdfRoot = path.join(workspaceRoot, "pdf");
const screenshotRoot = path.join(workspaceRoot, "screenshots");
const baselinePath = path.join(repoRoot, "tests", "ReportTemplateFixtures", "report_template_pdf_pixel_baselines.json");
const fingerprintGrid = {
  columns: 24,
  rows: 32,
  minInkRatio: 0.006,
};
const baselineDefaults = {
  maxFingerprintDistance: 24,
  maxPaperBoundsDelta: 10,
  maxInkBoundsDelta: 28,
};

const templateCases = createReportRegressionTemplateCases("pdfPixel");

function resolveHtmlPath(testCase) {
  if (testCase.relativePath) {
    const htmlPath = path.join(repoRoot, ...testCase.relativePath.split("/"));
    assert(fs.existsSync(htmlPath), `${testCase.slug}: expected HTML source to exist: ${htmlPath}`);
    assertTemplateSourcePageOrientation(testCase, htmlPath);
    return htmlPath;
  }

  const caseRoot = path.join(workspaceRoot, testCase.slug);
  fs.mkdirSync(caseRoot, { recursive: true });
  const htmlPath = path.join(caseRoot, `${testCase.slug}.html`);
  fs.writeFileSync(htmlPath, testCase.html, "utf8");
  return htmlPath;
}

function renderPdf(chromePath, testCase) {
  const profilePath = path.join(workspaceRoot, `PrintProfile-${testCase.slug}`);
  const htmlPath = resolveHtmlPath(testCase);
  const pdfPath = path.join(pdfRoot, `${testCase.slug}.pdf`);

  fs.rmSync(profilePath, { recursive: true, force: true });
  fs.mkdirSync(profilePath, { recursive: true });
  fs.mkdirSync(pdfRoot, { recursive: true });

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
  assert(result.status === 0, `${testCase.slug}: Chrome PDF print exited with status ${result.status}:\n${output}`);
  assert(fs.existsSync(pdfPath), `${testCase.slug}: PDF was not created.`);
  assert(
    fs.statSync(pdfPath).size >= testCase.minPdfBytes,
    `${testCase.slug}: PDF size ${fs.statSync(pdfPath).size} below ${testCase.minPdfBytes}.`,
  );
  const pageCount = countPdfPages(pdfPath);
  assert(
    pageCount === testCase.expectedPages,
    `${testCase.slug}: expected ${testCase.expectedPages} PDF page(s), found ${pageCount}.`,
  );

  return { htmlPath, pdfPath };
}

function countPdfPages(pdfPath) {
  const content = fs.readFileSync(pdfPath).toString("latin1");
  return (content.match(/\/Type\s*\/Page\b/g) || []).length;
}

async function capturePdfScreenshots(chromePath, renderedCases) {
  const profilePath = path.join(workspaceRoot, "PdfViewerProfile");
  fs.rmSync(profilePath, { recursive: true, force: true });
  fs.mkdirSync(profilePath, { recursive: true });
  fs.mkdirSync(screenshotRoot, { recursive: true });

  const child = spawn(
    chromePath,
    [
      "--headless=new",
      "--disable-gpu",
      "--disable-extensions",
      "--disable-background-networking",
      "--no-first-run",
      "--hide-scrollbars",
      "--force-device-scale-factor=1",
      "--font-render-hinting=none",
      "--remote-debugging-port=0",
      `--user-data-dir=${profilePath}`,
      "--window-size=900,1270",
      "about:blank",
    ],
    {
      stdio: ["ignore", "pipe", "pipe"],
      windowsHide: true,
    },
  );

  let browserWebSocketUrl;
  const results = [];
  try {
    browserWebSocketUrl = await waitForDevToolsUrl(child, "pdf-pixel-regression");
    const pageWebSocketUrl = await getPageWebSocketUrl(browserWebSocketUrl, "pdf-pixel-regression");
    const page = await CdpClient.connect(pageWebSocketUrl);
    try {
      await page.send("Page.enable");
      await page.send("Runtime.enable");

      for (const renderedCase of renderedCases) {
        const { testCase, pdfPath } = renderedCase;
        await page.send("Emulation.setDeviceMetricsOverride", {
          width: testCase.viewport.width,
          height: testCase.viewport.height,
          deviceScaleFactor: 1,
          mobile: false,
        });

        for (let pageNumber = 1; pageNumber <= testCase.expectedPages; pageNumber += 1) {
          const screenshotPath = path.join(screenshotRoot, `${testCase.slug}.page-${pageNumber}.pdf-viewer.png`);
          const loadEvent = page.waitForEvent("Page.loadEventFired", () => true, 15000).catch(() => null);
          const pdfUrl = `${pathToFileURL(pdfPath).href}#page=${pageNumber}&zoom=page-fit`;
          const navigation = await page.send("Page.navigate", { url: pdfUrl });
          assert(!navigation.isDownload, `${testCase.slug} page ${pageNumber}: PDF unexpectedly started as a download.`);
          await loadEvent;
          await delay(2500);

          const location = await page.send("Runtime.evaluate", {
            expression: "location.href",
            returnByValue: true,
          });
          assert(
            String(location?.result?.value || "").toLowerCase().includes(`${testCase.slug}.pdf`.toLowerCase()),
            `${testCase.slug} page ${pageNumber}: PDF viewer did not navigate to the expected file.`,
          );

          const screenshot = await page.send("Page.captureScreenshot", {
            format: "png",
            fromSurface: true,
            captureBeyondViewport: false,
          });
          assert(screenshot.data, `${testCase.slug} page ${pageNumber}: Chrome did not return screenshot data.`);
          fs.writeFileSync(screenshotPath, Buffer.from(screenshot.data, "base64"));
          results.push({ ...renderedCase, pageNumber, screenshotPath });
        }
      }
    } finally {
      page.close();
    }
  } finally {
    await closeChrome(browserWebSocketUrl, child);
  }

  return results;
}

function analyzePdfViewerScreenshot(screenshotPath) {
  const image = parsePng(fs.readFileSync(screenshotPath));
  const paper = findLargestWhiteComponent(image);
  assert(paper, `${screenshotPath}: unable to locate the rendered PDF paper area.`);

  const insetX = Math.max(4, Math.floor(paper.width * 0.015));
  const insetY = Math.max(4, Math.floor(paper.height * 0.015));
  const inner = {
    left: Math.min(paper.right, paper.left + insetX),
    top: Math.min(paper.bottom, paper.top + insetY),
    right: Math.max(paper.left, paper.right - insetX),
    bottom: Math.max(paper.top, paper.bottom - insetY),
  };

  let darkPixelsInsidePaper = 0;
  let nonWhitePixelsInsidePaper = 0;
  let inkMinX = image.width;
  let inkMinY = image.height;
  let inkMaxX = -1;
  let inkMaxY = -1;
  const colorBuckets = new Set();

  for (let y = inner.top; y <= inner.bottom; y += 1) {
    for (let x = inner.left; x <= inner.right; x += 1) {
      const offset = (y * image.width + x) * 4;
      const r = image.pixels[offset];
      const g = image.pixels[offset + 1];
      const b = image.pixels[offset + 2];
      const a = image.pixels[offset + 3];
      if (a === 0) {
        continue;
      }

      const isWhite = r >= 248 && g >= 248 && b >= 248;
      const luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
      if (!isWhite) {
        nonWhitePixelsInsidePaper += 1;
        colorBuckets.add(`${r >> 4}:${g >> 4}:${b >> 4}`);
      }

      if (luma < 150) {
        darkPixelsInsidePaper += 1;
        inkMinX = Math.min(inkMinX, x);
        inkMinY = Math.min(inkMinY, y);
        inkMaxX = Math.max(inkMaxX, x);
        inkMaxY = Math.max(inkMaxY, y);
      }
    }
  }

  const totalPixels = image.width * image.height;
  return {
    width: image.width,
    height: image.height,
    totalPixels,
    paperBounds: {
      left: paper.left,
      top: paper.top,
      right: paper.right,
      bottom: paper.bottom,
      width: paper.width,
      height: paper.height,
      whitePixels: paper.count,
      whiteRatio: roundRatio(paper.count / totalPixels),
    },
    darkPixelsInsidePaper,
    nonWhitePixelsInsidePaper,
    colorBucketCountInsidePaper: colorBuckets.size,
    inkBounds:
      inkMaxX >= 0
        ? {
            left: inkMinX,
            top: inkMinY,
            right: inkMaxX,
            bottom: inkMaxY,
            width: inkMaxX - inkMinX + 1,
            height: inkMaxY - inkMinY + 1,
          }
        : null,
    inkFingerprint: buildInkFingerprint(image, inner, fingerprintGrid),
  };
}

function buildInkFingerprint(image, bounds, grid) {
  const bits = [];
  let occupiedCells = 0;
  const boundedWidth = Math.max(1, bounds.right - bounds.left + 1);
  const boundedHeight = Math.max(1, bounds.bottom - bounds.top + 1);

  for (let row = 0; row < grid.rows; row += 1) {
    const yStart = Math.floor(bounds.top + (row * boundedHeight) / grid.rows);
    const yEnd = Math.max(yStart, Math.floor(bounds.top + ((row + 1) * boundedHeight) / grid.rows) - 1);

    for (let column = 0; column < grid.columns; column += 1) {
      const xStart = Math.floor(bounds.left + (column * boundedWidth) / grid.columns);
      const xEnd = Math.max(xStart, Math.floor(bounds.left + ((column + 1) * boundedWidth) / grid.columns) - 1);
      let cellPixels = 0;
      let inkPixels = 0;

      for (let y = yStart; y <= yEnd; y += 1) {
        for (let x = xStart; x <= xEnd; x += 1) {
          const offset = (y * image.width + x) * 4;
          const alpha = image.pixels[offset + 3];
          if (alpha === 0) {
            continue;
          }

          cellPixels += 1;
          const r = image.pixels[offset];
          const g = image.pixels[offset + 1];
          const b = image.pixels[offset + 2];
          const isWhite = r >= 248 && g >= 248 && b >= 248;
          if (!isWhite) {
            inkPixels += 1;
          }
        }
      }

      const minInkPixels = Math.max(4, Math.ceil(cellPixels * grid.minInkRatio));
      const isOccupied = inkPixels >= minInkPixels;
      bits.push(isOccupied ? 1 : 0);
      if (isOccupied) {
        occupiedCells += 1;
      }
    }
  }

  return {
    columns: grid.columns,
    rows: grid.rows,
    minInkRatio: grid.minInkRatio,
    occupiedCells,
    hex: bitsToHex(bits),
  };
}

function findLargestWhiteComponent(image) {
  const { width, height, pixels } = image;
  const totalPixels = width * height;
  const visited = new Uint8Array(totalPixels);
  const stack = new Int32Array(totalPixels);
  let best = null;

  for (let index = 0; index < totalPixels; index += 1) {
    if (visited[index] || !isWhitePixel(pixels, index)) {
      visited[index] = 1;
      continue;
    }

    let stackLength = 0;
    stack[stackLength] = index;
    stackLength += 1;
    visited[index] = 1;
    let count = 0;
    let minX = width;
    let minY = height;
    let maxX = -1;
    let maxY = -1;

    while (stackLength > 0) {
      stackLength -= 1;
      const current = stack[stackLength];
      const x = current % width;
      const y = Math.floor(current / width);
      count += 1;
      minX = Math.min(minX, x);
      minY = Math.min(minY, y);
      maxX = Math.max(maxX, x);
      maxY = Math.max(maxY, y);

      for (const next of [current - 1, current + 1, current - width, current + width]) {
        if (next < 0 || next >= totalPixels || visited[next]) {
          continue;
        }

        const nextX = next % width;
        if ((next === current - 1 && nextX !== x - 1) || (next === current + 1 && nextX !== x + 1)) {
          continue;
        }

        visited[next] = 1;
        if (isWhitePixel(pixels, next)) {
          stack[stackLength] = next;
          stackLength += 1;
        }
      }
    }

    if (!best || count > best.count) {
      best = {
        count,
        left: minX,
        top: minY,
        right: maxX,
        bottom: maxY,
        width: maxX - minX + 1,
        height: maxY - minY + 1,
      };
    }
  }

  return best;
}

function isWhitePixel(pixels, pixelIndex) {
  const offset = pixelIndex * 4;
  return pixels[offset + 3] !== 0 && pixels[offset] >= 245 && pixels[offset + 1] >= 245 && pixels[offset + 2] >= 245;
}

function assertPdfPixelMetrics(testCase, pageNumber, metrics) {
  const label = `${testCase.slug} page ${pageNumber}`;
  assert(metrics.width === testCase.viewport.width, `${label}: PDF screenshot width mismatch.`);
  assert(metrics.height === testCase.viewport.height, `${label}: PDF screenshot height mismatch.`);
  assert(
    metrics.paperBounds.whiteRatio >= testCase.minPaperWhiteRatio,
    `${label}: paper white ratio ${metrics.paperBounds.whiteRatio} below ${testCase.minPaperWhiteRatio}.`,
  );
  assert(
    metrics.paperBounds.width >= testCase.viewport.width * testCase.minPaperWidthRatio,
    `${label}: paper width ${metrics.paperBounds.width} below expected ratio ${testCase.minPaperWidthRatio}.`,
  );
  assert(
    metrics.paperBounds.height >= testCase.viewport.height * testCase.minPaperHeightRatio,
    `${label}: paper height ${metrics.paperBounds.height} below expected ratio ${testCase.minPaperHeightRatio}.`,
  );
  assert(
    metrics.darkPixelsInsidePaper >= testCase.minDarkPixelsInsidePaper,
    `${label}: dark pixels inside PDF paper ${metrics.darkPixelsInsidePaper} below ${testCase.minDarkPixelsInsidePaper}.`,
  );
  assert(
    metrics.nonWhitePixelsInsidePaper >= testCase.minNonWhitePixelsInsidePaper,
    `${label}: non-white pixels inside PDF paper ${metrics.nonWhitePixelsInsidePaper} below ${testCase.minNonWhitePixelsInsidePaper}.`,
  );
  assert(
    metrics.colorBucketCountInsidePaper >= testCase.minColorBucketsInsidePaper,
    `${label}: PDF paper color buckets ${metrics.colorBucketCountInsidePaper} below ${testCase.minColorBucketsInsidePaper}.`,
  );
  assert(metrics.inkBounds, `${label}: expected dark ink bounds inside PDF paper.`);
}

function loadPdfPixelBaselines() {
  assert(fs.existsSync(baselinePath), `PDF pixel baseline file is missing: ${baselinePath}`);
  const baseline = JSON.parse(fs.readFileSync(baselinePath, "utf8"));
  assert(baseline && baseline.version === 1, "PDF pixel baseline file has an unsupported version.");
  assert(baseline.pages && typeof baseline.pages === "object", "PDF pixel baseline file is missing pages.");
  assert(
    baseline.grid?.columns === fingerprintGrid.columns &&
      baseline.grid?.rows === fingerprintGrid.rows &&
      baseline.grid?.minInkRatio === fingerprintGrid.minInkRatio,
    "PDF pixel baseline grid does not match the current fingerprint settings.",
  );
  return baseline;
}

function buildPdfPixelBaseline(results) {
  const pages = {};
  for (const result of results) {
    pages[pdfPixelBaselineKey(result.slug, result.pageNumber)] = {
      fingerprintHex: result.metrics.inkFingerprint.hex,
      occupiedCells: result.metrics.inkFingerprint.occupiedCells,
      paperBounds: pickBounds(result.metrics.paperBounds),
      inkBounds: pickBounds(result.metrics.inkBounds),
    };
  }

  return {
    version: 1,
    description:
      "Golden layout fingerprints for PDF viewer screenshots generated from legacy/customer and built-in report templates.",
    grid: fingerprintGrid,
    defaults: baselineDefaults,
    pages,
  };
}

function assertPdfPixelBaselines(results, baseline) {
  const actualKeys = new Set(results.map((result) => pdfPixelBaselineKey(result.slug, result.pageNumber)));
  const baselineKeys = new Set(Object.keys(baseline.pages));
  for (const baselineKey of baselineKeys) {
    assert(actualKeys.has(baselineKey), `PDF pixel baseline has no rendered page in this run: ${baselineKey}`);
  }

  for (const result of results) {
    const key = pdfPixelBaselineKey(result.slug, result.pageNumber);
    const expected = baseline.pages[key];
    assert(expected, `PDF pixel baseline is missing rendered page: ${key}`);
    const options = {
      ...baselineDefaults,
      ...(baseline.defaults ?? {}),
      ...(expected.tolerance ?? {}),
    };

    const distance = hexHammingDistance(result.metrics.inkFingerprint.hex, expected.fingerprintHex);
    assert(
      distance <= options.maxFingerprintDistance,
      `${key}: PDF page ink fingerprint distance ${distance} exceeds ${options.maxFingerprintDistance}.`,
    );

    assertBoundsWithinTolerance(
      `${key} paper`,
      result.metrics.paperBounds,
      expected.paperBounds,
      options.maxPaperBoundsDelta,
    );
    assertBoundsWithinTolerance(
      `${key} ink`,
      result.metrics.inkBounds,
      expected.inkBounds,
      options.maxInkBoundsDelta,
    );
  }
}

function pdfPixelBaselineKey(slug, pageNumber) {
  return `${slug}#${pageNumber}`;
}

async function run() {
  fs.rmSync(workspaceRoot, { recursive: true, force: true });
  fs.mkdirSync(workspaceRoot, { recursive: true });

  const pdfChromePath = locateChromeForTesting(repoRoot, "headless-shell");
  const viewerChromePath = locateChromeForTesting(repoRoot, "full-chrome");
  const renderedCases = templateCases.map((testCase) => ({
    testCase,
    ...renderPdf(pdfChromePath, testCase),
  }));

  const screenshotCases = await capturePdfScreenshots(viewerChromePath, renderedCases);
  const results = [];
  for (const screenshotCase of screenshotCases) {
    const metrics = analyzePdfViewerScreenshot(screenshotCase.screenshotPath);
    results.push({
      slug: screenshotCase.testCase.slug,
      pageNumber: screenshotCase.pageNumber,
      expectedPages: screenshotCase.testCase.expectedPages,
      templatePath: screenshotCase.testCase.relativePath ?? screenshotCase.htmlPath,
      pdfPath: screenshotCase.pdfPath,
      screenshotPath: screenshotCase.screenshotPath,
      metrics,
    });
  }

  const actualBaseline = buildPdfPixelBaseline(results);
  const actualBaselinePath = path.join(workspaceRoot, "pdf-pixel-baseline.actual.json");
  fs.writeFileSync(actualBaselinePath, `${JSON.stringify(actualBaseline, null, 2)}\n`, "utf8");

  const summaryPath = path.join(workspaceRoot, "pdf-pixel-regression-summary.json");
  fs.writeFileSync(
    summaryPath,
    `${JSON.stringify(
      {
        pdfBrowserExecutable: pdfChromePath,
        pdfViewerExecutable: viewerChromePath,
        results,
      },
      null,
      2,
    )}\n`,
    "utf8",
  );

  for (const result of results) {
    const testCase = templateCases.find((item) => item.slug === result.slug);
    assert(testCase, `${result.slug}: missing test case definition.`);
    assertPdfPixelMetrics(testCase, result.pageNumber, result.metrics);
  }
  assertPdfPixelBaselines(results, loadPdfPixelBaselines());

  process.stdout.write(`report-template-pdf-pixel-regression test passed (${results.length} PDF page screenshots)\n`);
  process.stdout.write(`summary: ${summaryPath}\n`);
  process.stdout.write(`actual baseline: ${actualBaselinePath}\n`);
}

try {
  await run();
} catch (error) {
  process.stderr.write(`${error.stack || error.message}\n`);
  process.exit(1);
}

