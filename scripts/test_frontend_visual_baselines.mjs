import { spawn } from "node:child_process";
import { copyFileSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { fileURLToPath, pathToFileURL } from "node:url";
import { CdpClient, closeChrome, delay } from "./lib/chromium-cdp.mjs";
import { captureScreenshot, createPageSession, evaluate, getFreePort, startChrome } from "./lib/web-runtime-browser-session.mjs";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const webRoot = path.join(repositoryRoot, "apps", "export-doc-web");
const webRequire = createRequire(path.join(webRoot, "package.json"));
const { default: pixelmatch } = await import(pathToFileURL(webRequire.resolve("pixelmatch")).href);
const { PNG } = webRequire("pngjs");
const outputRoot = path.join(repositoryRoot, "artifacts", "frontend-visual-baseline");
const approvedRoot = path.join(webRoot, "visual-baselines", "approved");
const diffRoot = path.join(outputRoot, "diffs");
const profileRoot = path.join(repositoryRoot, ".codex-runtime", "visual-baseline-chrome");
const browserExecutable = path.join(repositoryRoot, "Browsers", "ChromeForTesting", "win64", "ChromeHeadlessShell", "chrome-headless-shell-win64", "chrome-headless-shell.exe");
const axeSource = readFileSync(path.join(webRoot, "node_modules", "axe-core", "axe.min.js"), "utf8");
const updateApprovedBaselines = process.env.UPDATE_FRONTEND_VISUAL_BASELINES === "1";
const maximumPixelDifferenceRatio = Number(process.env.FRONTEND_VISUAL_MAX_DIFF_RATIO ?? "0.001");
const pages = ["login", "login-expired", "dashboard", "invoice", "invoiceParties", "hs", "singleWindow", "report", "state-loading", "state-empty", "state-error", "state-fatal", "state-offline", "state-offline-local", "state-permission", "state-conflict", "state-feedback", "dialog"];
const viewports = [
  { name: "desktop-1366", width: 1366, height: 768 },
  { name: "desktop-1920", width: 1920, height: 1080 },
  { name: "tablet-1024", width: 1024, height: 768 },
  { name: "mobile-390", width: 390, height: 844 },
];

mkdirSync(outputRoot, { recursive: true });
mkdirSync(approvedRoot, { recursive: true });
rmSync(diffRoot, { recursive: true, force: true });
mkdirSync(diffRoot, { recursive: true });
rmSync(profileRoot, { recursive: true, force: true });
mkdirSync(profileRoot, { recursive: true });

const port = await getFreePort();
const viteCommand = process.platform === "win32" ? (process.env.ComSpec || "cmd.exe") : "npm";
const viteArguments = process.platform === "win32"
  ? ["/d", "/s", "/c", `npm run dev -- --port ${port} --strictPort`]
  : ["run", "dev", "--", "--port", String(port), "--strictPort"];
const vite = spawn(viteCommand, viteArguments, {
  cwd: webRoot,
  stdio: ["ignore", "pipe", "pipe"],
  windowsHide: true,
});
let viteOutput = "";
vite.stdout.on("data", (chunk) => { viteOutput += chunk.toString(); });
vite.stderr.on("data", (chunk) => { viteOutput += chunk.toString(); });

let chrome;
let cdp;
const results = [];
try {
  await waitForHttp(`http://127.0.0.1:${port}/visual-baseline.html`);
  chrome = await startChrome({ browserExecutable, userDataDir: profileRoot, timeoutMs: 30000 });
  cdp = await CdpClient.connect(chrome.browserWebSocketUrl);
  const page = await createPageSession(cdp);

  for (const viewport of viewports) {
    await page.send("Emulation.setDeviceMetricsOverride", {
      width: viewport.width,
      height: viewport.height,
      deviceScaleFactor: 1,
      mobile: false,
    });
    await page.send("Emulation.setEmulatedMedia", {
      media: "screen",
      features: [{ name: "prefers-reduced-motion", value: "reduce" }],
    });
    for (const pageName of pages) {
      const url = `http://127.0.0.1:${port}/visual-baseline.html?page=${pageName}`;
      await page.send("Page.navigate", { url });
      await waitForReady(page);
      await evaluate(page, `(() => {
        const style = document.createElement("style");
        style.dataset.visualRegressionStability = "true";
        style.textContent = "*,*::before,*::after{animation:none!important;transition:none!important;caret-color:transparent!important;scroll-behavior:auto!important}";
        document.head.appendChild(style);
        document.activeElement?.blur?.();
      })()`, false);
      await delay(100);
      await evaluate(page, axeSource, false);
      const axeAudit = await evaluate(page, `(async () => {
        const result = await axe.run(document, { resultTypes: ["violations"] });
        return result.violations
          .filter((violation) => violation.impact === "critical" || violation.impact === "serious")
          .map((violation) => ({
            id: violation.id,
            impact: violation.impact,
            help: violation.help,
            nodes: violation.nodes.slice(0, 8).map((node) => ({ target: node.target, summary: node.failureSummary })),
          }));
      })()`, true);
      const audit = await evaluate(page, `(() => {
        const root = document.documentElement;
        const visible = (selector) => {
          const element = document.querySelector(selector);
          return !!element && getComputedStyle(element).display !== "none" && element.getBoundingClientRect().width > 0;
        };
        const unnamedButtons = [...document.querySelectorAll("button")].filter((button) =>
          !((button.textContent || "").trim() || button.getAttribute("aria-label") || button.getAttribute("title"))
        ).length;
        const unlabeledInputs = [...document.querySelectorAll("input, select, textarea")].filter((control) =>
          !(control.getAttribute("aria-label") || control.getAttribute("aria-labelledby") || control.closest("label"))
        ).length;
        const truncatedCriticalText = [...document.querySelectorAll("[data-visual-critical-text]")]
          .filter((element) => element.scrollWidth > element.clientWidth + 1)
          .map((element) => (element.textContent || "").trim());
        const partyControlOverflow = ${JSON.stringify(pageName)} === "invoiceParties"
          ? [...document.querySelectorAll(".invoice-party-group input, .invoice-party-group select, .invoice-party-group textarea")]
            .filter((control) => {
              const group = control.closest(".invoice-party-group");
              if (!group) return true;
              const controlRect = control.getBoundingClientRect();
              const groupRect = group.getBoundingClientRect();
              return controlRect.left < groupRect.left - 1 || controlRect.right > groupRect.right + 1;
            }).length
          : 0;
        const reportLayout = ${JSON.stringify(pageName)} === "report"
          ? (() => {
              const selectors = [".template-selection-panel", ".template-user-panel", ".template-admin-panel", ".template-package-panel"];
              const panels = selectors.map((selector) => document.querySelector(selector));
              const rects = panels.map((panel) => panel?.getBoundingClientRect()).filter(Boolean);
              const selectionRect = rects[0];
              const managementRects = rects.slice(1);
              const workspaceWidth = document.querySelector(".report-template-surface")?.getBoundingClientRect().width ?? 0;
              const overlapCount = rects.flatMap((rect, index) => rects.slice(index + 1).map((other) => ({ rect, other })))
                .filter(({ rect, other }) => Math.min(rect.right, other.right) - Math.max(rect.left, other.left) > 1
                  && Math.min(rect.bottom, other.bottom) - Math.max(rect.top, other.top) > 1).length;
              const selectionFieldWidths = [...document.querySelectorAll(".template-selection-panel > label")]
                .map((field) => field.getBoundingClientRect().width);
              const responsivePlacementMatches = workspaceWidth <= 1160
                ? managementRects.every((rect) => selectionRect && rect.top >= selectionRect.bottom - 1)
                : managementRects.every((rect) => selectionRect && Math.abs(rect.top - selectionRect.top) <= 1);
              return {
                overlapCount,
                minimumSelectionFieldWidth: selectionFieldWidths.length > 0 ? Math.min(...selectionFieldWidths) : 0,
                responsivePlacementMatches,
              };
            })()
          : { overlapCount: 0, minimumSelectionFieldWidth: 999, responsivePlacementMatches: true };
        const isShelllessPage = ["login", "login-expired", "state-fatal"].includes(${JSON.stringify(pageName)});
        const compactNavigationWidth = !isShelllessPage && ${viewport.width} >= 861 && ${viewport.width} <= 1180
          ? document.querySelector(".workspace-nav")?.getBoundingClientRect().width ?? 1000
          : null;
        const connectivityNoticeMatches = ${JSON.stringify(pageName)} === "state-offline"
          ? visible(".workspace-connectivity-notice")
          : ${JSON.stringify(pageName)} === "state-offline-local"
            ? !visible(".workspace-connectivity-notice")
            : true;
        return {
          title: document.title,
          scrollWidth: root.scrollWidth,
          clientWidth: root.clientWidth,
          horizontalOverflow: root.scrollWidth > root.clientWidth + 1,
          unnamedButtons,
          unlabeledInputs,
          truncatedCriticalText,
          partyControlOverflow,
          reportPanelOverlapCount: reportLayout.overlapCount,
          reportMinimumSelectionFieldWidth: reportLayout.minimumSelectionFieldWidth,
          reportResponsivePlacementMatches: reportLayout.responsivePlacementMatches,
          compactNavigationWidth,
          connectivityNoticeMatches,
          invoiceDetailColumnCount: ${JSON.stringify(pageName)} === "invoice" ? document.querySelectorAll(".item-editor-table thead th").length : null,
          workspaceNavigation: isShelllessPage ? true : visible(".workspace-nav"),
          expectedStickyControl: ${JSON.stringify(pageName)} === "invoice" ? visible(".invoice-editor-sticky-actions")
            : ${JSON.stringify(pageName)} === "report" ? visible(".report-template-sticky-header") : true,
          expectedDialog: ${JSON.stringify(pageName)} === "dialog" ? visible('[role="dialog"][aria-modal="true"]') : true,
        };
      })()`, true);
      const value = audit.value;
      const screenshotPath = path.join(outputRoot, `${pageName}-${viewport.name}.png`);
      await captureScreenshot(page, screenshotPath);
      const approvedPath = path.join(approvedRoot, path.basename(screenshotPath));
      if (updateApprovedBaselines) copyFileSync(screenshotPath, approvedPath);
      const pixelComparison = compareScreenshot({ screenshotPath, approvedPath, diffRoot, maximumPixelDifferenceRatio });
      const axeViolations = axeAudit.value ?? [];
      const passed = !value.horizontalOverflow && value.unnamedButtons === 0 && value.unlabeledInputs === 0
        && axeViolations.length === 0
        && value.truncatedCriticalText.length === 0
        && value.partyControlOverflow === 0
        && value.reportPanelOverlapCount === 0
        && value.reportMinimumSelectionFieldWidth >= 128
        && value.reportResponsivePlacementMatches
        && (value.compactNavigationWidth === null || value.compactNavigationWidth <= 80)
        && value.connectivityNoticeMatches
        && (value.invoiceDetailColumnCount === null || value.invoiceDetailColumnCount >= 20)
        && value.workspaceNavigation && value.expectedStickyControl && value.expectedDialog
        && pixelComparison.passed;
      results.push({ page: pageName, viewport, url, screenshotPath, approvedPath, pixelComparison, passed, axeViolations, ...value });
    }
  }

  const summary = {
    generatedAt: new Date().toISOString(),
    browserExecutable,
    approvedBaselinesUpdated: updateApprovedBaselines,
    maximumPixelDifferenceRatio,
    passed: results.every((result) => result.passed),
    results,
  };
  writeFileSync(path.join(outputRoot, "summary.json"), `${JSON.stringify(summary, null, 2)}\n`, "utf8");
  if (!summary.passed) {
    const failed = results.filter((result) => !result.passed).map((result) => `${result.page}/${result.viewport.name}: ${JSON.stringify(result)}`);
    throw new Error(`Frontend visual baseline checks failed:\n${failed.join("\n")}`);
  }
  console.log(`frontend visual baselines passed (${results.length} scenes)`);
} finally {
  cdp?.close();
  if (chrome) await closeChrome(chrome.browserWebSocketUrl, chrome.process);
  await stopProcessTree(vite);
  await delay(250);
  rmSync(profileRoot, { recursive: true, force: true });
}

function compareScreenshot({ screenshotPath, approvedPath, diffRoot, maximumPixelDifferenceRatio }) {
  if (!existsSync(approvedPath)) {
    return { status: "missing-approved-baseline", passed: false, differentPixels: null, differenceRatio: null, diffPath: null };
  }
  const actual = PNG.sync.read(readFileSync(screenshotPath));
  const approved = PNG.sync.read(readFileSync(approvedPath));
  if (actual.width !== approved.width || actual.height !== approved.height) {
    return {
      status: "dimension-mismatch",
      passed: false,
      actualSize: `${actual.width}x${actual.height}`,
      approvedSize: `${approved.width}x${approved.height}`,
      differentPixels: actual.width * actual.height,
      differenceRatio: 1,
      diffPath: null,
    };
  }
  const diff = new PNG({ width: actual.width, height: actual.height });
  const differentPixels = pixelmatch(actual.data, approved.data, diff.data, actual.width, actual.height, {
    threshold: 0.1,
    includeAA: false,
  });
  const differenceRatio = differentPixels / (actual.width * actual.height);
  const passed = differenceRatio <= maximumPixelDifferenceRatio;
  const diffPath = passed ? null : path.join(diffRoot, path.basename(screenshotPath));
  if (diffPath) writeFileSync(diffPath, PNG.sync.write(diff));
  return { status: passed ? "matched" : "pixel-difference", passed, differentPixels, differenceRatio, diffPath };
}

async function waitForHttp(url) {
  for (let attempt = 0; attempt < 120; attempt += 1) {
    if (vite.exitCode !== null) throw new Error(`Vite exited before startup.\n${viteOutput}`);
    const response = await fetch(url).catch(() => null);
    if (response?.ok) return;
    await delay(100);
  }
  throw new Error(`Timed out waiting for Vite.\n${viteOutput}`);
}

async function waitForReady(page) {
  for (let attempt = 0; attempt < 150; attempt += 1) {
    const result = await evaluate(page, "document.documentElement.dataset.visualBaselineReady === 'true'", true).catch(() => null);
    if (result?.value === true) {
      await delay(150);
      return;
    }
    await delay(100);
  }
  throw new Error("Visual baseline page did not become ready.");
}

async function stopProcessTree(child) {
  if (child.exitCode !== null || child.signalCode !== null) return;
  if (process.platform === "win32") {
    await new Promise((resolve) => {
      const killer = spawn("taskkill.exe", ["/pid", String(child.pid), "/T", "/F"], {
        stdio: "ignore",
        windowsHide: true,
      });
      killer.once("exit", resolve);
      killer.once("error", resolve);
    });
    return;
  }
  child.kill("SIGTERM");
}
