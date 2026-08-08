import { spawn } from "node:child_process";
import { createHash } from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { buildChromiumSandboxArguments } from "./chromium-sandbox-policy.mjs";
import {
  CdpClient,
  closeChrome,
  delay,
  getPageWebSocketUrl,
  waitForDevToolsUrl,
} from "./chromium-cdp.mjs";

export async function capturePdfViewerPages({
  chromePath,
  renderedCases,
  workspaceRoot,
  screenshotRoot,
}) {
  const profilePath = path.join(workspaceRoot, "PdfViewerProfile");
  fs.rmSync(profilePath, { recursive: true, force: true });
  fs.mkdirSync(profilePath, { recursive: true });
  fs.mkdirSync(screenshotRoot, { recursive: true });

  const child = spawn(
    chromePath,
    [
      ...buildChromiumSandboxArguments(),
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
  let browser;
  const results = [];
  try {
    browserWebSocketUrl = await waitForDevToolsUrl(child, "pdf-pixel-regression");
    browser = await CdpClient.connect(browserWebSocketUrl);
    const pageWebSocketUrl = await getPageWebSocketUrl(browserWebSocketUrl, "pdf-pixel-regression");
    const pageTargetId = pageWebSocketUrl.slice(pageWebSocketUrl.lastIndexOf("/") + 1);
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

        const loadEvent = page.waitForEvent("Page.loadEventFired", () => true, 15000).catch(() => null);
        const pdfUrl = `${pathToFileURL(pdfPath).href}#page=1&zoom=page-fit`;
        const navigation = await page.send("Page.navigate", { url: pdfUrl });
        assertCapture(!navigation.isDownload, `${testCase.slug}: PDF unexpectedly started as a download.`);
        await loadEvent;

        const location = await page.send("Runtime.evaluate", {
          expression: "location.href",
          returnByValue: true,
        });
        assertCapture(
          String(location?.result?.value || "").toLowerCase().includes(`${testCase.slug}.pdf`.toLowerCase()),
          `${testCase.slug}: PDF viewer did not navigate to the expected file.`,
        );

        const viewerSessionId = await attachPdfViewerSession(browser, pageTargetId, testCase);
        try {
          for (let pageNumber = 1; pageNumber <= testCase.expectedPages; pageNumber += 1) {
            await navigatePdfViewerPage(browser, viewerSessionId, testCase, pageNumber);
            const screenshotPath = path.join(screenshotRoot, `${testCase.slug}.page-${pageNumber}.pdf-viewer.png`);
            const screenshot = await page.send("Page.captureScreenshot", {
              format: "png",
              fromSurface: true,
              captureBeyondViewport: false,
            });
            assertCapture(screenshot.data, `${testCase.slug} page ${pageNumber}: Chrome did not return screenshot data.`);
            const screenshotBytes = Buffer.from(screenshot.data, "base64");
            fs.writeFileSync(screenshotPath, screenshotBytes);
            results.push({
              ...renderedCase,
              pageNumber,
              screenshotPath,
              screenshotSha256: createHash("sha256").update(screenshotBytes).digest("hex"),
            });
          }
        } finally {
          await browser.send("Target.detachFromTarget", { sessionId: viewerSessionId }).catch(() => null);
        }
      }
    } finally {
      page.close();
    }
  } finally {
    browser?.close();
    await closeChrome(browserWebSocketUrl, child);
  }

  return results;
}

async function attachPdfViewerSession(browser, pageTargetId, testCase) {
  let sessionId;
  try {
    for (let attempt = 0; attempt < 100; attempt += 1) {
      const targets = await browser.send("Target.getTargets");
      const viewerTarget = targets.targetInfos?.find(
        (target) =>
          target.type === "iframe" &&
          target.parentId === pageTargetId &&
          target.url?.startsWith("chrome-extension://") &&
          target.url.endsWith("/index.html"),
      );
      if (!viewerTarget) {
        await delay(100);
        continue;
      }

      const attached = await browser.send("Target.attachToTarget", {
        targetId: viewerTarget.targetId,
        flatten: true,
      });
      sessionId = attached.sessionId;
      assertCapture(sessionId, `${testCase.slug}: Chrome did not return a PDF viewer session.`);
      await browser.send("Runtime.enable", {}, sessionId);
      break;
    }

    assertCapture(sessionId, `${testCase.slug}: Chrome did not expose its PDF viewer frame.`);
    for (let attempt = 0; attempt < 150; attempt += 1) {
      const state = await evaluatePdfViewer(browser, sessionId, `(() => {
        const viewer = document.querySelector("pdf-viewer");
        const pageCount = viewer?.documentDimensions?.pageDimensions?.length ?? 0;
        return {
          loadState: viewer?.loadState_ ?? null,
          pageCount,
          hasPageNavigation:
            typeof viewer?.goToPageAndXy_ === "function" ||
            typeof viewer?.viewport_?.goToPageAndXy === "function",
        };
      })()`);
      if (
        state?.loadState === "success" &&
        state.pageCount === testCase.expectedPages &&
        state.hasPageNavigation
      ) {
        return sessionId;
      }
      await delay(100);
    }

    throw new Error(
      `${testCase.slug}: PDF viewer did not finish loading ${testCase.expectedPages} page(s).`,
    );
  } catch (error) {
    if (sessionId) {
      await browser.send("Target.detachFromTarget", { sessionId }).catch(() => null);
    }
    throw error;
  }
}

async function navigatePdfViewerPage(browser, sessionId, testCase, pageNumber) {
  const pageIndex = pageNumber - 1;
  const navigation = await evaluatePdfViewer(browser, sessionId, `(() => {
    const viewer = document.querySelector("pdf-viewer");
    if (!viewer) {
      throw new Error("PDF viewer element is unavailable.");
    }

    if (typeof viewer.goToPageAndXy_ === "function") {
      viewer.goToPageAndXy_(null, ${pageIndex}, { x: 0, y: 0 });
    } else if (typeof viewer.viewport_?.goToPageAndXy === "function") {
      viewer.viewport_.goToPageAndXy(${pageIndex}, 0, 0);
    } else {
      throw new Error("PDF viewer page navigation API is unavailable.");
    }

    return { pageNo: viewer.pageNo_ ?? null };
  })()`);
  assertCapture(
    navigation?.pageNo === pageNumber,
    `${testCase.slug} page ${pageNumber}: PDF viewer selected page ${navigation?.pageNo ?? "unknown"}.`,
  );

  for (let attempt = 0; attempt < 50; attempt += 1) {
    const state = await evaluatePdfViewer(browser, sessionId, `(() => {
      const viewer = document.querySelector("pdf-viewer");
      return { pageNo: viewer?.pageNo_ ?? null };
    })()`);
    if (state?.pageNo === pageNumber) {
      await delay(500);
      return;
    }
    await delay(100);
  }

  throw new Error(`${testCase.slug} page ${pageNumber}: PDF viewer did not settle on the requested page.`);
}

async function evaluatePdfViewer(browser, sessionId, expression) {
  const response = await browser.send(
    "Runtime.evaluate",
    {
      expression,
      returnByValue: true,
      awaitPromise: true,
    },
    sessionId,
  );
  if (response.exceptionDetails) {
    const message = response.exceptionDetails.exception?.description ?? response.exceptionDetails.text;
    throw new Error(`PDF viewer evaluation failed: ${message}`);
  }
  return response.result?.value;
}

function assertCapture(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
