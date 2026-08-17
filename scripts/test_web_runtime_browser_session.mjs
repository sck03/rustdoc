import assert from "node:assert/strict";
import { spawnProcessTree, stopProcessTree } from "./lib/child-process-tree.mjs";
import { CdpClient } from "./lib/chromium-cdp.mjs";
import { buildChromeLaunchArguments } from "./lib/web-runtime-browser-session.mjs";
import { createSmokeStageRunner } from "./lib/web-runtime-smoke-deadline.mjs";
import { parseWebRuntimeSmokeArgs, validateWebRuntimeSmokeOptions } from "./lib/web-runtime-smoke-options.mjs";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  parseWindowsBuild,
  shouldDisableChromiumSandbox,
} from "./lib/chromium-sandbox-policy.mjs";

assert.equal(
  typeof globalThis.WebSocket,
  "function",
  `Node.js ${process.versions.node} must satisfy the Node.js 24 CI baseline and provide the global WebSocket used by the Chrome DevTools client.`,
);

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const browserSessionSource = fs.readFileSync(path.join(repoRoot, "scripts", "lib", "web-runtime-browser-session.mjs"), "utf8");
const chromiumCdpSource = fs.readFileSync(path.join(repoRoot, "scripts", "lib", "chromium-cdp.mjs"), "utf8");
assert.match(browserSessionSource, /spawnProcessTree\(options\.browserExecutable/);
assert.match(browserSessionSource, /stopProcessTree\(chrome, 5000\)/);
assert.match(chromiumCdpSource, /stopProcessTree\(child, 5000\)/);

const smokeOptions = parseWebRuntimeSmokeArgs(["--global-timeout-ms", "5000"]);
validateWebRuntimeSmokeOptions({
  ...smokeOptions,
  browserExecutable: process.execPath,
  webUrl: "http://127.0.0.1:5173/",
  apiBaseUrl: "http://127.0.0.1:5174/",
  username: "admin",
  userDataDir: repoRoot,
  expectedText: ["ready"],
});
assert.equal(smokeOptions.globalTimeoutMs, 5000);

const stageEvents = [];
const runSmokeStage = createSmokeStageRunner(1000, 25, (message) => stageEvents.push(message));
let boundedStageTimeout = 0;
assert.equal(await runSmokeStage("contract", (timeoutMs) => {
  boundedStageTimeout = timeoutMs;
  return "ok";
}), "ok");
assert(boundedStageTimeout > 0 && boundedStageTimeout <= 25);
assert.match(stageEvents[0], /contract started/);
assert.match(stageEvents[1], /contract completed/);

const options = {
  browserExecutable: "/repo/Browsers/chrome-headless-shell",
  userDataDir: "/repo/.codex-runtime/browser-profile",
};

const linuxCiArguments = buildChromeLaunchArguments(options, {
  platform: "linux",
  isCi: true,
  isRoot: false,
});
assert(linuxCiArguments.includes("--remote-debugging-address=127.0.0.1"));
assert(linuxCiArguments.includes("--remote-debugging-port=0"));
assert(linuxCiArguments.includes("--disable-dev-shm-usage"));
assert(linuxCiArguments.includes("--no-sandbox"));
assert(linuxCiArguments.includes("--force-device-scale-factor=1"));
assert(!linuxCiArguments.includes("--headless=new"));

const linuxDeveloperArguments = buildChromeLaunchArguments(options, {
  platform: "linux",
  isCi: false,
  isRoot: false,
});
assert(linuxDeveloperArguments.includes("--disable-dev-shm-usage"));
assert(!linuxDeveloperArguments.includes("--no-sandbox"));

const windowsChromeArguments = buildChromeLaunchArguments(
  { ...options, browserExecutable: "C:\\repo\\chrome.exe" },
  { platform: "win32", isCi: true, isRoot: false, windowsBuild: 22631 },
);
assert(windowsChromeArguments.includes("--headless=new"));
assert(!windowsChromeArguments.includes("--disable-dev-shm-usage"));
assert(!windowsChromeArguments.includes("--no-sandbox"));

const legacyWindowsArguments = buildChromeLaunchArguments(options, {
  platform: "win32",
  isCi: false,
  isRoot: false,
  windowsBuild: 17763,
});
assert(legacyWindowsArguments.includes("--no-sandbox"));
assert.equal(parseWindowsBuild("10.0.17763"), 17763);
assert.equal(parseWindowsBuild("invalid"), null);
assert.equal(shouldDisableChromiumSandbox({
  platform: "win32",
  windowsBuild: 17763,
  noSandboxSetting: "false",
}), false);
assert.equal(shouldDisableChromiumSandbox({
  platform: "win32",
  windowsBuild: 22631,
  noSandboxSetting: "true",
}), true);

class FakeSocket extends EventTarget {
  constructor({ respond }) {
    super();
    this.respond = respond;
  }

  send(payload) {
    if (!this.respond) return;
    const { id } = JSON.parse(payload);
    queueMicrotask(() => {
      this.dispatchEvent(new MessageEvent("message", {
        data: JSON.stringify({ id, result: { acknowledged: true } }),
      }));
    });
  }

  close() {
    this.dispatchEvent(new Event("close"));
  }
}

const responsiveClient = new CdpClient(new FakeSocket({ respond: true }), 25);
assert.deepEqual(await responsiveClient.send("Runtime.enable"), { acknowledged: true });
assert.equal(responsiveClient.pending.size, 0);
responsiveClient.close();

const stalledClient = new CdpClient(new FakeSocket({ respond: false }), 25);
await assert.rejects(
  stalledClient.send("Runtime.evaluate", {}, "scale-contract-session"),
  /Timed out waiting for DevTools command: Runtime\.evaluate in session scale-contract-session\./,
);
assert.equal(stalledClient.pending.size, 0);
stalledClient.close();

const processTree = spawnProcessTree(process.execPath, [
  "-e",
  "const { spawn } = require('node:child_process'); const child = spawn(process.execPath, ['-e', 'setInterval(() => {}, 1000)'], { stdio: 'ignore' }); console.log(child.pid); setInterval(() => {}, 1000);",
], { stdio: ["ignore", "pipe", "ignore"], windowsHide: true });
const descendantPid = await new Promise((resolve, reject) => {
  let output = "";
  const timer = setTimeout(() => reject(new Error("Timed out waiting for the process-tree test child.")), 3000);
  processTree.stdout.on("data", (chunk) => {
    output += chunk.toString();
    const value = Number.parseInt(output.trim(), 10);
    if (Number.isInteger(value)) {
      clearTimeout(timer);
      resolve(value);
    }
  });
  processTree.once("error", reject);
});
await stopProcessTree(processTree, 3000);
assert.equal(isProcessAlive(processTree.pid), false, "Process tree root must exit during cleanup.");
assert.equal(isProcessAlive(descendantPid), false, "Process tree descendants must exit during cleanup.");

process.stdout.write("web-runtime-browser-session tests passed\n");

function isProcessAlive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    if (error?.code === "ESRCH") return false;
    throw error;
  }
}
