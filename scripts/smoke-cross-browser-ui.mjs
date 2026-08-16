import {
  cpSync,
  existsSync,
  mkdirSync,
  readFileSync,
  rmSync,
} from "node:fs";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { spawnProcessTree, stopProcessTree } from "./lib/child-process-tree.mjs";
import { resolveDotnetCommand } from "./lib/dotnet-command.mjs";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const playwrightBrowsersPath = path.resolve(
  process.env.PLAYWRIGHT_BROWSERS_PATH || path.join(repositoryRoot, "artifacts", "playwright-browsers"),
);
process.env.PLAYWRIGHT_BROWSERS_PATH = playwrightBrowsersPath;
const requestedBrowsers = readBrowserArgument();
const legacyWindowsFirefoxSandboxDisabled = shouldDisableLegacyWindowsFirefoxSandbox();
const closeTimeoutMs = 10_000;
const browserCloseTimeoutMs = 30_000;
const runtimeIdentifier = `${platformPrefix()}-${architectureName()}`;
const apiOutputRoot = path.join(
  repositoryRoot,
  "src",
  "ExportDocManager.Api",
  "bin",
  "Release",
  "net10.0",
  runtimeIdentifier,
);
const apiDll = path.join(apiOutputRoot, "ExportDocManager.Api.dll");
const playwrightEntry = path.join(apiOutputRoot, ".playwright", "package", "index.mjs");
const webDist = path.join(repositoryRoot, "apps", "export-doc-web", "dist");
const axeSource = path.join(repositoryRoot, "apps", "export-doc-web", "node_modules", "axe-core", "axe.min.js");
for (const requiredPath of [apiDll, playwrightEntry, path.join(webDist, "index.html"), axeSource]) {
  if (!existsSync(requiredPath)) {
    throw new Error(`Cross-browser prerequisite is missing: ${requiredPath}`);
  }
}
if (!existsSync(playwrightBrowsersPath)) {
  throw new Error(
    `Playwright browser cache is missing: ${playwrightBrowsersPath}. ` +
    "Install Firefox and WebKit into PLAYWRIGHT_BROWSERS_PATH before running this acceptance check.",
  );
}

const playwright = await import(pathToFileURL(playwrightEntry).href);
const runtimeRoot = path.join(repositoryRoot, "artifacts", "cross-browser-ui");
const appRoot = path.join(runtimeRoot, "app");
const dataRoot = path.join(runtimeRoot, "data");
rmSync(runtimeRoot, { recursive: true, force: true });
mkdirSync(appRoot, { recursive: true });
mkdirSync(dataRoot, { recursive: true });
cpSync(webDist, path.join(appRoot, "wwwroot"), { recursive: true });
if (!existsSync(path.join(appRoot, "wwwroot", "index.html"))) {
  throw new Error("Cross-browser frontend staging did not produce app/wwwroot/index.html");
}

const port = await getFreePort();
const baseUrl = `http://127.0.0.1:${port}`;
const apiProcess = spawnProcessTree(
  resolveDotnetCommand(),
  [apiDll, "--app-root", appRoot, "--data-root", dataRoot, "--urls", baseUrl],
  {
    cwd: repositoryRoot,
    env: {
      ...process.env,
      DOTNET_CLI_TELEMETRY_OPTOUT: "1",
      DOTNET_NOLOGO: "1",
      Logging__LogLevel__Default: "Warning",
      "Logging__LogLevel__Microsoft.EntityFrameworkCore": "Warning",
    },
    stdio: ["ignore", "pipe", "pipe"],
    windowsHide: true,
  },
);
let apiOutput = "";
apiProcess.stdout.on("data", (chunk) => { apiOutput += chunk.toString(); });
apiProcess.stderr.on("data", (chunk) => { apiOutput += chunk.toString(); });

try {
  await waitForHealth(`${baseUrl}/healthz`, 30_000);
  const results = [];
  const cleanupWarnings = [];
  for (const browserName of requestedBrowsers) {
    const browserType = playwright[browserName];
    if (!browserType) throw new Error(`Unsupported Playwright browser: ${browserName}`);
    const operationTimeout = browserOperationTimeout(browserName);
    const launchOptions = { headless: true, timeout: operationTimeout };
    if (browserName === "firefox" && legacyWindowsFirefoxSandboxDisabled) {
      // Firefox 151's content sandbox cannot spawn its tab process on the
      // still-supported Windows 10 LTSC 2019 kernel. This acceptance worker
      // only opens the loopback test server and is deleted after the run.
      launchOptions.env = { ...process.env, MOZ_DISABLE_CONTENT_SANDBOX: "1" };
    }
    const browser = await withTimeout(
      browserType.launch(launchOptions),
      operationTimeout + 5_000,
      `${browserName} launch`,
    );
    try {
      results.push(await runViewportAcceptance(browser, browserName, baseUrl, axeSource, {
        name: "desktop",
        width: 1440,
        height: 1000,
      }));
      results.push(await runViewportAcceptance(browser, browserName, baseUrl, axeSource, {
        name: "mobile",
        width: 390,
        height: 844,
      }));
    } finally {
      const warning = await closeBrowser(browser, browserName);
      if (warning) cleanupWarnings.push(warning);
    }
  }
  process.stdout.write(`${JSON.stringify({
    success: true,
    runtimeIdentifier,
    playwrightBrowsersPath,
    legacyWindowsFirefoxSandboxDisabled,
    cleanupWarnings,
    results,
  }, null, 2)}\n`);
} catch (error) {
  if (apiOutput.trim()) process.stderr.write(`API output:\n${apiOutput}\n`);
  throw error;
} finally {
  let cleanupError;
  try {
    if (!await stopProcessTree(apiProcess, closeTimeoutMs)) {
      cleanupError = new Error(`API process tree ${apiProcess.pid} did not exit within the cleanup deadline`);
    }
  } catch (error) {
    cleanupError = error;
  } finally {
    if (cleanupError) {
      apiProcess.stdout.destroy();
      apiProcess.stderr.destroy();
      apiProcess.unref();
    }
    rmSync(runtimeRoot, { recursive: true, force: true });
  }
  if (cleanupError) throw cleanupError;
}

async function closeBrowser(browser, browserName) {
  const closeResult = Promise.resolve().then(() => browser.close()).then(
    () => ({ error: null }),
    (error) => ({ error }),
  );
  const result = await withTimeout(
    closeResult,
    browserCloseTimeoutMs,
    `${browserName} close`,
    false,
  );
  if (result === false) {
    const warning = `${browserName} close exceeded ${browserCloseTimeoutMs}ms after its acceptance checks passed`;
    process.stderr.write(`Warning: ${warning}\n`);
    return warning;
  }
  if (result.error) {
    const message = result.error instanceof Error ? result.error.message : String(result.error);
    const warning = `${browserName} close reported a cleanup error after its acceptance checks passed: ${message}`;
    process.stderr.write(`Warning: ${warning}\n`);
    return warning;
  }
  return "";
}

async function runViewportAcceptance(browser, browserName, baseUrl, axeSource, viewport) {
  const operationTimeout = browserOperationTimeout(browserName);
  const context = await withTimeout(
    browser.newContext({
      viewport: { width: viewport.width, height: viewport.height },
      locale: "zh-CN",
      timezoneId: "Asia/Shanghai",
    }),
    operationTimeout,
    `${browserName}/${viewport.name} context creation`,
  );
  await withTimeout(
    context.addInitScript({ path: axeSource }),
    operationTimeout,
    `${browserName}/${viewport.name} init script`,
  );
  const page = await withTimeout(
    context.newPage(),
    operationTimeout,
    `${browserName}/${viewport.name} page creation`,
  );
  page.setDefaultTimeout(operationTimeout);
  page.setDefaultNavigationTimeout(operationTimeout);
  const pageErrors = [];
  const serverErrors = [];
  page.on("pageerror", (error) => pageErrors.push(error.message));
  page.on("response", (response) => {
    if (response.status() >= 500) serverErrors.push(`${response.status()} ${response.url()}`);
  });
  try {
    await page.goto(baseUrl, { waitUntil: "domcontentloaded", timeout: operationTimeout });
    await page.locator('input[autocomplete="username"]').fill("admin");
    await page.locator('input[autocomplete="current-password"]').fill("");
    await page.getByRole("button", { name: "登录" }).click();
    await page.locator(".app-shell").waitFor({ state: "visible", timeout: operationTimeout });

    const metrics = {
      ...await withTimeout(page.evaluate(() => {
        const shell = document.querySelector(".app-shell");
        const mobileToggle = document.querySelector(".mobile-nav-toggle");
        return {
          title: document.querySelector(".workspace-header h1")?.textContent?.trim() || "",
          device: shell?.getAttribute("data-workspace-device") || "",
          mobileToggleVisible: mobileToggle instanceof HTMLElement &&
            getComputedStyle(mobileToggle).display !== "none" &&
            mobileToggle.getBoundingClientRect().width > 0,
        };
      }), operationTimeout, `${browserName}/${viewport.name} workspace metrics`),
      ...await readHorizontalOverflowDiagnostics(page, operationTimeout),
    };
    const accessibility = await withTimeout(page.evaluate(async () => {
      const axe = globalThis.axe;
      if (!axe) throw new Error("axe-core was not injected");
      const report = await axe.run(document, {
        resultTypes: ["violations"],
        runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21aa"] },
      });
      return report.violations
        .filter((violation) => ["critical", "serious"].includes(violation.impact || ""))
        .map((violation) => ({
          id: violation.id,
          impact: violation.impact,
          description: violation.description,
          targets: violation.nodes.slice(0, 5).map((node) => node.target.join(" ")),
        }));
    }), operationTimeout, `${browserName}/${viewport.name} accessibility scan`);

    if (!metrics.title) throw new Error(`${browserName}/${viewport.name}: workspace title is empty`);
    if (metrics.horizontalOverflow > 1) {
      throw new Error(`${browserName}/${viewport.name}: document overflows horizontally by ${metrics.horizontalOverflow}px`);
    }
    if (viewport.name === "mobile" && (!metrics.mobileToggleVisible || metrics.device !== "phone")) {
      throw new Error(
        `${browserName}/mobile: simplified phone navigation contract was not activated: ` +
        JSON.stringify(metrics),
      );
    }
    if (viewport.name === "desktop" && metrics.device !== "desktop") {
      throw new Error(
        `${browserName}/desktop: desktop viewport was classified incorrectly: ` +
        JSON.stringify(metrics),
      );
    }
    const lazyRouteStyles = await validateLazyRouteStyles(
      page,
      browserName,
      viewport.name,
      baseUrl,
      operationTimeout,
    );
    if (pageErrors.length || serverErrors.length || accessibility.length) {
      throw new Error(
        `${browserName}/${viewport.name} acceptance failed: ` +
        JSON.stringify({ pageErrors, serverErrors, accessibility }),
      );
    }

    return {
      browser: browserName,
      viewport: viewport.name,
      workspaceTitle: metrics.title,
      deviceMode: metrics.device,
      seriousAccessibilityViolations: accessibility.length,
      pageErrors: pageErrors.length,
      serverErrors: serverErrors.length,
      lazyRouteStyles,
    };
  } finally {
    await withTimeout(
      context.close(),
      closeTimeoutMs,
      `${browserName}/${viewport.name} context close`,
    );
  }
}

async function validateLazyRouteStyles(page, browserName, viewportName, baseUrl, operationTimeout) {
  const routes = [
    {
      name: "single-window",
      url: `${baseUrl}/#/single-window/operation-center`,
      selector: ".single-window-surface",
      expectedProperty: "overflowY",
      expectedValue: "auto",
    },
    {
      name: "reports",
      url: `${baseUrl}/#/reports/templates`,
      selector: ".report-template-layout",
      expectedProperty: "display",
      expectedValue: "grid",
    },
    {
      name: "container-packing",
      url: `${baseUrl}/#/tools/container-packing`,
      selector: ".container-packing-surface",
      expectedProperty: "overflowY",
      expectedValue: "auto",
    },
  ];
  const results = [];
  for (const route of routes) {
    await page.goto(route.url, { waitUntil: "domcontentloaded", timeout: operationTimeout });
    await page.locator(route.selector).waitFor({ state: "visible", timeout: operationTimeout });
    await page.waitForFunction(
      ({ selector, expectedProperty, expectedValue }) => {
        const element = document.querySelector(selector);
        return element instanceof HTMLElement && getComputedStyle(element)[expectedProperty] === expectedValue;
      },
      {
        selector: route.selector,
        expectedProperty: route.expectedProperty,
        expectedValue: route.expectedValue,
      },
      { timeout: operationTimeout },
    );
    const metrics = {
      ...await withTimeout(page.evaluate(({ selector, expectedProperty }) => {
        const element = document.querySelector(selector);
        if (!(element instanceof HTMLElement)) return null;
        const style = getComputedStyle(element);
        return {
          computedValue: style[expectedProperty],
        };
      }, { selector: route.selector, expectedProperty: route.expectedProperty }),
      operationTimeout,
      `${browserName}/${viewportName}/${route.name} style metrics`),
      ...await readHorizontalOverflowDiagnostics(page, operationTimeout),
    };
    if (!metrics || metrics.computedValue !== route.expectedValue || metrics.horizontalOverflow > 1) {
      throw new Error(
        `${browserName}/${viewportName}/${route.name}: lazy route CSS contract failed: ` +
        JSON.stringify({ route, metrics }),
      );
    }
    results.push({ name: route.name, ...metrics });
  }
  return results;
}

async function readHorizontalOverflowDiagnostics(page, timeoutMs) {
  return withTimeout(page.evaluate(() => {
    const horizontalOverflow = document.documentElement.scrollWidth - document.documentElement.clientWidth;
    if (horizontalOverflow <= 1) {
      return { horizontalOverflow, overflowingElements: [], overflowAncestors: [] };
    }

    const viewportWidth = document.documentElement.clientWidth;
    const candidates = [...document.querySelectorAll("body *")]
      .filter((candidate) => candidate instanceof HTMLElement)
      .map((candidate) => {
        const rect = candidate.getBoundingClientRect();
        const style = getComputedStyle(candidate);
        return {
          element: candidate,
          selector: describeElement(candidate),
          left: Math.round(rect.left),
          right: Math.round(rect.right),
          width: Math.round(rect.width),
          clientWidth: candidate.clientWidth,
          scrollWidth: candidate.scrollWidth,
          display: style.display,
          gridTemplateColumns: style.gridTemplateColumns,
        };
      })
      .filter(({ left, right, width }) => width > 0 && (left < -1 || right > viewportWidth + 1))
      .sort((left, right) => right.right - left.right)
      .slice(0, 10);

    return {
      horizontalOverflow,
      overflowingElements: candidates.map(({ element: _element, ...details }) => details),
      overflowAncestors: describeAncestors(candidates[0]?.element),
    };

    function describeAncestors(candidate) {
      const ancestors = [];
      for (let current = candidate; current instanceof HTMLElement && ancestors.length < 8; current = current.parentElement) {
        const rect = current.getBoundingClientRect();
        const style = getComputedStyle(current);
        ancestors.push({
          selector: describeElement(current),
          left: Math.round(rect.left),
          right: Math.round(rect.right),
          width: Math.round(rect.width),
          clientWidth: current.clientWidth,
          scrollWidth: current.scrollWidth,
          display: style.display,
          gridTemplateColumns: style.gridTemplateColumns,
          containerType: style.containerType,
        });
      }
      return ancestors;
    }

    function describeElement(element) {
      const id = element.id ? `#${element.id}` : "";
      const classes = [...element.classList].slice(0, 4).map((name) => `.${name}`).join("");
      return `${element.tagName.toLowerCase()}${id}${classes}`;
    }
  }), timeoutMs, "horizontal overflow diagnostics");
}

function readBrowserArgument() {
  const argument = process.argv.find((value) => value.startsWith("--browsers="));
  const values = String(argument?.slice("--browsers=".length) || "firefox,webkit")
    .split(",")
    .map((value) => value.trim().toLowerCase())
    .filter(Boolean);
  if (values.length === 0 || values.some((value) => !["firefox", "webkit"].includes(value))) {
    throw new Error("--browsers must contain firefox and/or webkit");
  }
  return [...new Set(values)];
}

function platformPrefix() {
  if (process.platform === "win32") return "win";
  if (process.platform === "linux") return "linux";
  if (process.platform === "darwin") return "osx";
  throw new Error(`Unsupported platform: ${process.platform}`);
}

function architectureName() {
  if (process.arch === "x64") return "x64";
  if (process.arch === "arm64") return "arm64";
  throw new Error(`Unsupported architecture: ${process.arch}`);
}

function shouldDisableLegacyWindowsFirefoxSandbox() {
  if (process.platform !== "win32") return false;
  const build = Number.parseInt(os.release().split(".")[2] || "", 10);
  return Number.isFinite(build) && build < 19041;
}

function browserOperationTimeout(browserName) {
  return process.platform === "win32" && browserName === "webkit" && shouldDisableLegacyWindowsFirefoxSandbox()
    ? 90_000
    : 30_000;
}

async function getFreePort() {
  return await new Promise((resolve, reject) => {
    const server = net.createServer();
    server.once("error", reject);
    server.listen(0, "127.0.0.1", () => {
      const address = server.address();
      const port = typeof address === "object" && address ? address.port : 0;
      server.close(() => resolve(port));
    });
  });
}

async function waitForHealth(url, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      const response = await fetch(url, {
        signal: AbortSignal.timeout(Math.min(2_000, Math.max(1, deadline - Date.now()))),
      });
      if (response.ok) return;
    } catch {
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error(`API did not become healthy within ${timeoutMs}ms: ${url}`);
}

async function withTimeout(promise, timeoutMs, label, throwOnTimeout = true) {
  let timer;
  const timeout = new Promise((resolve, reject) => {
    timer = setTimeout(() => {
      if (throwOnTimeout) reject(new Error(`${label} timed out after ${timeoutMs}ms`));
      else resolve(false);
    }, timeoutMs);
  });
  try {
    return await Promise.race([promise, timeout]);
  } finally {
    clearTimeout(timer);
  }
}
