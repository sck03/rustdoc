import { spawn } from "node:child_process";
import {
  cpSync,
  existsSync,
  mkdirSync,
  readFileSync,
  rmSync,
} from "node:fs";
import net from "node:net";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const playwrightBrowsersPath = path.resolve(
  process.env.PLAYWRIGHT_BROWSERS_PATH || path.join(repositoryRoot, "artifacts", "playwright-browsers"),
);
process.env.PLAYWRIGHT_BROWSERS_PATH = playwrightBrowsersPath;
const requestedBrowsers = readBrowserArgument();
const runtimeIdentifier = `${platformPrefix()}-${architectureName()}`;
const apiOutputRoot = path.join(
  repositoryRoot,
  "src",
  "ExportDocManager.Api",
  "bin",
  "Release",
  "net8.0",
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
const apiProcess = spawn(
  "dotnet",
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
  for (const browserName of requestedBrowsers) {
    const browserType = playwright[browserName];
    if (!browserType) throw new Error(`Unsupported Playwright browser: ${browserName}`);
    const browser = await browserType.launch({ headless: true });
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
      await browser.close();
    }
  }
  process.stdout.write(`${JSON.stringify({ success: true, runtimeIdentifier, playwrightBrowsersPath, results }, null, 2)}\n`);
} catch (error) {
  if (apiOutput.trim()) process.stderr.write(`API output:\n${apiOutput}\n`);
  throw error;
} finally {
  await stopChild(apiProcess);
  rmSync(runtimeRoot, { recursive: true, force: true });
}

async function runViewportAcceptance(browser, browserName, baseUrl, axeSource, viewport) {
  const context = await browser.newContext({
    viewport: { width: viewport.width, height: viewport.height },
    locale: "zh-CN",
    timezoneId: "Asia/Shanghai",
  });
  await context.addInitScript({ path: axeSource });
  const page = await context.newPage();
  const pageErrors = [];
  const serverErrors = [];
  page.on("pageerror", (error) => pageErrors.push(error.message));
  page.on("response", (response) => {
    if (response.status() >= 500) serverErrors.push(`${response.status()} ${response.url()}`);
  });
  try {
    await page.goto(baseUrl, { waitUntil: "networkidle", timeout: 30_000 });
    await page.locator('input[autocomplete="username"]').fill("admin");
    await page.locator('input[autocomplete="current-password"]').fill("");
    await page.getByRole("button", { name: "登录" }).click();
    await page.locator(".app-shell").waitFor({ state: "visible", timeout: 30_000 });

    const metrics = await page.evaluate(() => {
      const shell = document.querySelector(".app-shell");
      const mobileToggle = document.querySelector(".mobile-nav-toggle");
      return {
        title: document.querySelector(".workspace-header h1")?.textContent?.trim() || "",
        device: shell?.getAttribute("data-workspace-device") || "",
        horizontalOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
        mobileToggleVisible: mobileToggle instanceof HTMLElement &&
          getComputedStyle(mobileToggle).display !== "none" &&
          mobileToggle.getBoundingClientRect().width > 0,
      };
    });
    const accessibility = await page.evaluate(async () => {
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
    });

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
    };
  } finally {
    await context.close();
  }
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
      const response = await fetch(url);
      if (response.ok) return;
    } catch {
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error(`API did not become healthy within ${timeoutMs}ms: ${url}`);
}

async function stopChild(child) {
  if (child.exitCode !== null || child.signalCode !== null) return;
  child.kill();
  await Promise.race([
    new Promise((resolve) => child.once("exit", resolve)),
    new Promise((resolve) => setTimeout(resolve, 5000)),
  ]);
  if (child.exitCode === null && child.signalCode === null) child.kill("SIGKILL");
}
