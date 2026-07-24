import { spawn } from "node:child_process";
import { mkdirSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { CdpClient, closeChrome, delay } from "./lib/chromium-cdp.mjs";
import { locateChromeForTesting } from "./lib/report-regression-common.mjs";
import { captureScreenshot, createPageSession, evaluate, getFreePort, startChrome } from "./lib/web-runtime-browser-session.mjs";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const webRoot = path.join(repositoryRoot, "apps", "export-doc-web");
const outputRoot = path.join(repositoryRoot, "artifacts", "frontend-scale-contracts");
const profileRoot = path.join(repositoryRoot, ".codex-runtime", "frontend-scale-contracts-chrome");
const browserExecutable = locateChromeForTesting(repositoryRoot, "headless-shell");
const pages = ["login", "dashboard", "invoice", "invoiceParties", "hs", "report", "singleWindow"];
const densities = ["comfortable", "compact"];
const allProfiles = [
  { name: "windows-125", width: 1366, height: 768, deviceScaleFactor: 1.25, mobile: false },
  { name: "windows-150", width: 1920, height: 1080, deviceScaleFactor: 1.5, mobile: false },
  { name: "windows-4k", width: 3840, height: 2160, deviceScaleFactor: 1.5, mobile: false },
  { name: "macos-retina", width: 1440, height: 900, deviceScaleFactor: 2, mobile: false },
  { name: "linux-100", width: 1366, height: 768, deviceScaleFactor: 1, mobile: false },
  { name: "mobile-safari-contract", width: 390, height: 844, deviceScaleFactor: 3, mobile: true },
];
const requestedProfileNames = new Set(
  String(process.env.EXPORTDOCMANAGER_SCALE_PROFILE_FILTER || "")
    .split(",")
    .map((value) => value.trim())
    .filter(Boolean),
);
const profiles = requestedProfileNames.size > 0
  ? allProfiles.filter((profile) => requestedProfileNames.has(profile.name))
  : allProfiles;
if (profiles.length === 0) {
  throw new Error(`No scale profiles matched EXPORTDOCMANAGER_SCALE_PROFILE_FILTER: ${[...requestedProfileNames].join(", ")}`);
}

rmSync(outputRoot, { recursive: true, force: true });
rmSync(profileRoot, { recursive: true, force: true });
mkdirSync(outputRoot, { recursive: true });
mkdirSync(profileRoot, { recursive: true });

const port = await getFreePort();
const viteCommand = process.platform === "win32" ? (process.env.ComSpec || "cmd.exe") : "npm";
const viteArguments = process.platform === "win32"
  ? ["/d", "/s", "/c", `npm run dev -- --port ${port} --strictPort`]
  : ["run", "dev", "--", "--port", String(port), "--strictPort"];
const vite = spawn(viteCommand, viteArguments, { cwd: webRoot, stdio: ["ignore", "pipe", "pipe"], windowsHide: true });
let viteOutput = "";
vite.stdout.on("data", (chunk) => { viteOutput += chunk.toString(); });
vite.stderr.on("data", (chunk) => { viteOutput += chunk.toString(); });

let chrome;
let cdp;
const results = [];
try {
  await waitForHttp(`http://127.0.0.1:${port}/visual-baseline.html`);
  chrome = await startChrome({ browserExecutable, userDataDir: profileRoot, timeoutMs: 60000 });
  cdp = await CdpClient.connect(chrome.browserWebSocketUrl);
  const page = await createPageSession(cdp);

  for (const profile of profiles) {
    process.stdout.write(`[scale] Starting profile ${profile.name}.\n`);
    await page.send("Emulation.setDeviceMetricsOverride", {
      width: profile.width,
      height: profile.height,
      deviceScaleFactor: profile.deviceScaleFactor,
      mobile: profile.mobile,
    });
    await page.send("Emulation.setEmulatedMedia", {
      media: "screen",
      features: [{ name: "prefers-reduced-motion", value: "reduce" }],
    });

    for (const density of densities) {
      for (const pageName of pages) {
        const scene = `${profile.name}/${density}/${pageName}`;
        process.stdout.write(`[scale] Running ${scene}.\n`);
        const url = `http://127.0.0.1:${port}/visual-baseline.html?page=${pageName}&density=${density}`;
        await page.send("Page.navigate", { url });
        await waitForReady(page);
        await evaluate(page, "document.fonts?.ready ?? Promise.resolve()", false);
        const audit = await evaluate(page, buildAuditExpression(profile.mobile), true);
        const value = audit.value;
        const passed = !value.horizontalOverflow
          && value.truncatedCriticalText.length === 0
          && value.reportPanelOverlapCount === 0
          && value.reportMinimumSelectionFieldWidth >= (profile.mobile ? 180 : 128)
          && value.mobileInputFontFailures.length === 0
          && value.mobileTouchTargetFailures.length === 0;

        let screenshotPath = null;
        if (pageName === "dashboard" || pageName === "report") {
          screenshotPath = path.join(outputRoot, `${pageName}-${profile.name}-${density}.png`);
          await captureScreenshot(page, screenshotPath, { captureBeyondViewport: false });
        }

        results.push({ page: pageName, density, profile, url, screenshotPath, passed, ...value });
        process.stdout.write(`[scale] ${passed ? "Passed" : "Failed"} ${scene}.\n`);
      }
    }
  }

  const summary = {
    generatedAt: new Date().toISOString(),
    browserExecutable,
    passed: results.every((result) => result.passed),
    results,
  };
  writeFileSync(path.join(outputRoot, "summary.json"), `${JSON.stringify(summary, null, 2)}\n`, "utf8");
  if (!summary.passed) {
    const failures = results.filter((result) => !result.passed).map((result) => `${result.profile.name}/${result.density}/${result.page}: ${JSON.stringify(result)}`);
    throw new Error(`Frontend scale contracts failed:\n${failures.join("\n")}`);
  }
  process.stdout.write(`Frontend scale contracts passed (${results.length} scenes).\n`);
} finally {
  cdp?.close();
  if (chrome) await closeChrome(chrome.browserWebSocketUrl, chrome.process);
  await stopProcessTree(vite);
  await delay(250);
  rmSync(profileRoot, { recursive: true, force: true });
}

function buildAuditExpression(isMobile) {
  return `(() => {
    const root = document.documentElement;
    const visible = (element) => {
      if (!(element instanceof HTMLElement)) return false;
      const style = getComputedStyle(element);
      const rect = element.getBoundingClientRect();
      return style.display !== "none" && style.visibility !== "hidden" && rect.width > 0 && rect.height > 0;
    };
    const truncatedCriticalText = [...document.querySelectorAll("[data-visual-critical-text]")]
      .filter((element) => element.scrollWidth > element.clientWidth + 1)
      .map((element) => (element.textContent || "").trim());
    const reportPanels = [...document.querySelectorAll(".template-selection-panel, .template-user-panel, .template-admin-panel, .template-package-panel")]
      .filter(visible)
      .map((element) => element.getBoundingClientRect());
    const reportPanelOverlapCount = reportPanels.flatMap((rect, index) => reportPanels.slice(index + 1).map((other) => ({ rect, other })))
      .filter(({ rect, other }) => Math.min(rect.right, other.right) - Math.max(rect.left, other.left) > 1
        && Math.min(rect.bottom, other.bottom) - Math.max(rect.top, other.top) > 1).length;
    const reportSelectionWidths = [...document.querySelectorAll(".template-selection-panel > label")]
      .filter(visible)
      .map((element) => element.getBoundingClientRect().width);
    const mobileInputFontFailures = ${isMobile}
      ? [...document.querySelectorAll("input, select, textarea")].filter(visible)
        .filter((element) => Number.parseFloat(getComputedStyle(element).fontSize) < 16)
        .map((element) => ({ tag: element.tagName, className: element.className, fontSize: getComputedStyle(element).fontSize }))
      : [];
    const frequentTargetSelector = ".command-button, .primary-button, .secondary-button, .icon-button, .nav-group-button, .nav-item, .settings-category-item, .master-data-tab, .report-template-workspace-tabs button, .segmented-control button, .density-toggle-button, .login-submit-button, .login-connection-settings summary";
    const mobileTouchTargetFailures = ${isMobile}
      ? [...document.querySelectorAll(frequentTargetSelector)].filter(visible)
        .filter((element) => {
          const rect = element.getBoundingClientRect();
          return rect.width < 43.5 || rect.height < 43.5;
        })
        .map((element) => ({ text: (element.textContent || element.getAttribute("aria-label") || "").trim(), width: element.getBoundingClientRect().width, height: element.getBoundingClientRect().height }))
      : [];
    return {
      horizontalOverflow: root.scrollWidth > root.clientWidth + 1,
      scrollWidth: root.scrollWidth,
      clientWidth: root.clientWidth,
      truncatedCriticalText,
      reportPanelOverlapCount,
      reportMinimumSelectionFieldWidth: reportSelectionWidths.length ? Math.min(...reportSelectionWidths) : 999,
      mobileInputFontFailures,
      mobileTouchTargetFailures,
      computedBodyFont: getComputedStyle(document.body).fontFamily,
      computedBodyFontSize: getComputedStyle(document.body).fontSize,
      interfaceDensity: root.dataset.interfaceDensity,
    };
  })()`;
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
      await delay(120);
      return;
    }
    await delay(100);
  }
  throw new Error("Visual scale page did not become ready.");
}

async function stopProcessTree(child) {
  if (child.exitCode !== null || child.signalCode !== null) return;
  if (process.platform === "win32") {
    await new Promise((resolve) => {
      const killer = spawn("taskkill.exe", ["/pid", String(child.pid), "/T", "/F"], { stdio: "ignore", windowsHide: true });
      killer.once("exit", resolve);
      killer.once("error", resolve);
    });
    return;
  }
  child.kill("SIGTERM");
}
