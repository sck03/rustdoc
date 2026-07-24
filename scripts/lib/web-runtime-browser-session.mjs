import { spawn } from "node:child_process";
import { writeFileSync } from "node:fs";
import net from "node:net";
import path from "node:path";
import { waitForDevToolsUrl } from "./chromium-cdp.mjs";

export async function startChrome(options) {
  const runtime = detectChromeRuntime();
  const args = buildChromeLaunchArguments(options, runtime);
  const chrome = spawn(options.browserExecutable, args, {
    stdio: ["ignore", "pipe", "pipe"],
    windowsHide: true,
  });

  try {
    const browserWebSocketUrl = await waitForDevToolsUrl(
      chrome,
      `Chrome (${options.browserExecutable})`,
      options.timeoutMs,
    );
    const debugPort = Number.parseInt(new URL(browserWebSocketUrl).port, 10);
    if (!Number.isInteger(debugPort) || debugPort <= 0) {
      throw new Error(`Chrome returned an invalid DevTools endpoint: ${browserWebSocketUrl}`);
    }

    return { process: chrome, debugPort, browserWebSocketUrl };
  } catch (error) {
    if (chrome.exitCode === null && chrome.signalCode === null) {
      chrome.kill();
      await waitForChildExit(chrome, 5000);
    }
    throw error;
  }
}

export function buildChromeLaunchArguments(options, runtime = detectChromeRuntime()) {
  const executableName = path.basename(options.browserExecutable).toLowerCase();
  const args = [
    "--remote-debugging-address=127.0.0.1",
    "--remote-debugging-port=0",
    `--user-data-dir=${options.userDataDir}`,
    "--disable-background-networking",
    "--disable-default-apps",
    "--disable-extensions",
    "--disable-gpu",
    "--disable-sync",
    "--metrics-recording-only",
    "--no-default-browser-check",
    "--no-first-run",
    "--window-size=1440,1000",
    "about:blank",
  ];

  if (!executableName.includes("headless")) {
    args.unshift("--headless=new");
  }

  if (runtime.platform === "linux") {
    args.unshift("--disable-dev-shm-usage");
  }

  if (runtime.platform === "linux" && (runtime.isCi || runtime.isRoot)) {
    args.unshift("--no-sandbox");
  }

  return args;
}

function detectChromeRuntime() {
  return {
    platform: process.platform,
    isCi: Boolean(process.env.CI),
    isRoot: typeof process.getuid === "function" && process.getuid() === 0,
  };
}

function waitForChildExit(child, timeoutMs) {
  if (child.exitCode !== null || child.signalCode !== null) {
    return Promise.resolve();
  }

  return new Promise((resolve) => {
    const timer = setTimeout(() => {
      child.off("exit", onExit);
      resolve();
    }, timeoutMs);
    function onExit() {
      clearTimeout(timer);
      resolve();
    }
    child.once("exit", onExit);
  });
}

export async function getFreePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.once("error", reject);
    server.listen(0, "127.0.0.1", () => {
      const address = server.address();
      const port = typeof address === "object" && address ? address.port : 0;
      server.close(() => resolve(port));
    });
  });
}

export async function createPageSession(cdp) {
  const { targetId } = await cdp.send("Target.createTarget", { url: "about:blank" });
  const { sessionId } = await cdp.send("Target.attachToTarget", { targetId, flatten: true });
  const page = {
    send: (method, params = {}) => cdp.send(method, params, sessionId),
  };

  await page.send("Page.enable");
  await page.send("Runtime.enable");
  return page;
}

export async function evaluate(page, expression, returnByValue, contextId = undefined) {
  const params = {
    expression,
    awaitPromise: true,
    returnByValue,
  };

  if (contextId) {
    params.contextId = contextId;
  }

  const result = await page.send("Runtime.evaluate", params);
  if (result.exceptionDetails) {
    throw new Error(`Runtime.evaluate failed: ${JSON.stringify(result.exceptionDetails)}`);
  }

  return result.result ?? {};
}

export async function captureScreenshot(page, screenshotPath, { captureBeyondViewport = true } = {}) {
  const result = await page.send("Page.captureScreenshot", {
    format: "png",
    captureBeyondViewport,
  });

  if (!result.data) {
    throw new Error("Chrome did not return screenshot data.");
  }

  writeFileSync(screenshotPath, Buffer.from(result.data, "base64"));
}
