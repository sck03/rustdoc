import { spawn } from "node:child_process";
import { writeFileSync } from "node:fs";
import net from "node:net";
import path from "node:path";
import { fetchJson, waitFor } from "./web-runtime-smoke-common.mjs";

export async function startChrome(options) {
  const debugPort = await getFreePort();
  const executableName = path.basename(options.browserExecutable).toLowerCase();
  const args = [
    `--remote-debugging-port=${debugPort}`,
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

  if (typeof process.getuid === "function" && process.getuid() === 0) {
    args.unshift("--no-sandbox");
  }

  const chrome = spawn(options.browserExecutable, args, {
    stdio: ["ignore", "pipe", "pipe"],
    windowsHide: true,
  });

  let stderr = "";
  chrome.stderr.on("data", (chunk) => {
    stderr += chunk.toString();
  });

  chrome.on("exit", (code, signal) => {
    if (code !== 0 && code !== null) {
      stderr += `\nChrome exited with code ${code}.`;
    }

    if (signal) {
      stderr += `\nChrome exited with signal ${signal}.`;
    }
  });

  const version = await waitFor(async () => {
    if (chrome.exitCode !== null) {
      throw new Error(`Chrome exited before DevTools was ready.${stderr ? `\n${stderr}` : ""}`);
    }

    const currentVersion = await fetchJson(`http://127.0.0.1:${debugPort}/json/version`).catch(() => null);
    return currentVersion?.webSocketDebuggerUrl ? currentVersion : null;
  }, options.timeoutMs, "Timed out waiting for Chrome DevTools.");

  return {
    process: chrome,
    debugPort,
    browserWebSocketUrl: version.webSocketDebuggerUrl,
  };
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

export async function captureScreenshot(page, screenshotPath) {
  const result = await page.send("Page.captureScreenshot", {
    format: "png",
    captureBeyondViewport: true,
  });

  if (!result.data) {
    throw new Error("Chrome did not return screenshot data.");
  }

  writeFileSync(screenshotPath, Buffer.from(result.data, "base64"));
}
