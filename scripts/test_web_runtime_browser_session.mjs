import assert from "node:assert/strict";
import { spawnProcessTree, stopProcessTree } from "./lib/child-process-tree.mjs";
import { CdpClient } from "./lib/chromium-cdp.mjs";
import { buildChromeLaunchArguments } from "./lib/web-runtime-browser-session.mjs";

assert.equal(
  typeof globalThis.WebSocket,
  "function",
  `Node.js ${process.versions.node} must satisfy the Node.js 24 CI baseline and provide the global WebSocket used by the Chrome DevTools client.`,
);

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
  { platform: "win32", isCi: true, isRoot: false },
);
assert(windowsChromeArguments.includes("--headless=new"));
assert(!windowsChromeArguments.includes("--disable-dev-shm-usage"));
assert(!windowsChromeArguments.includes("--no-sandbox"));

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
assert(processTree.exitCode !== null || processTree.signalCode !== null, "Process tree root must exit during cleanup.");
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
