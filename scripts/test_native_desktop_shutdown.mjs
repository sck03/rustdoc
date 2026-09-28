import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import fs from "node:fs";
import net from "node:net";
import path from "node:path";
import { promisify } from "node:util";
import { CdpClient, delay } from "./lib/chromium-cdp.mjs";
import { captureScreenshot, evaluate, getFreePort } from "./lib/web-runtime-browser-session.mjs";
import { spawnProcessTree, stopProcessTree } from "./lib/child-process-tree.mjs";

// Windows integration gate: run against a built package, with isolated writable data.
assert.equal(process.platform, "win32", "WebView2 lifecycle verification requires Windows");
const repo = path.resolve(import.meta.dirname, "..");
const appRoot = path.resolve(process.argv[2] || path.join(repo, "artifacts/native-desktop/ExportDocManager.Tauri"));
const executable = path.resolve(process.argv[3] || path.join(appRoot, "ExportDocManager.exe"));
const output = path.join(repo, "artifacts/desktop-shutdown", `${Date.now()}`);
fs.mkdirSync(output, { recursive: true });
const execute = promisify(execFile);
const sessions = [];
const results = [];
const run = async (session, expression) => (await evaluate(session.cdp, expression, true)).value;

async function processes(session) {
  // Pass paths as environment data, never interpolate them into PowerShell code.
  const command = `
    $all = @(Get-CimInstance Win32_Process)
    $ids = [Collections.Generic.HashSet[uint32]]::new()
    [void]$ids.Add([uint32]$env:EDM_TEST_PID)
    do {
      $added = $false
      foreach ($entry in $all) {
        if ($ids.Contains($entry.ParentProcessId)) { $added = $ids.Add($entry.ProcessId) -or $added }
      }
    } while ($added)
    $all | Where-Object {
      $ids.Contains($_.ProcessId) -or
      ($_.Name -eq 'msedgewebview2.exe' -and $_.CommandLine -and
       $_.CommandLine.Contains($env:EDM_TEST_PROFILE, [StringComparison]::OrdinalIgnoreCase))
    } | Select-Object ProcessId,ParentProcessId,Name,CreationDate,WorkingSetSize | ConvertTo-Json -Compress
  `;
  const { stdout } = await execute("pwsh", ["-NoProfile", "-NonInteractive", "-Command", command], {
    windowsHide: true, timeout: 15000,
    env: { ...process.env, EDM_TEST_PID: String(session.child.pid), EDM_TEST_PROFILE: path.join(session.dataRoot, "WebView") },
  });
  const entries = stdout.trim() ? JSON.parse(stdout) : [];
  return Array.isArray(entries) ? entries : [entries];
}

async function start(name, dataRoot = path.join(output, name)) {
  const port = await getFreePort();
  const session = { name, dataRoot, child: null, cdp: null, exit: null };
  session.child = spawnProcessTree(executable, ["--app-root", appRoot, "--data-root", dataRoot], {
    cwd: appRoot, windowsHide: true, stdio: "ignore",
    env: {
      ...process.env,
      TEMP: path.join(repo, ".codex-runtime/temp"), TMP: path.join(repo, ".codex-runtime/temp"),
      EXPORTDOCMANAGER_DESKTOP_SMOKE: "1", EXPORTDOCMANAGER_DESKTOP_TOKEN: "isolated-shutdown-test",
      WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-address=127.0.0.1 --remote-debugging-port=${port}`,
    },
  });
  sessions.push(session);
  session.child.once("error", error => { session.failure = error; });
  session.child.once("exit", (code, signal) => { session.exit = { code, signal }; });
  const deadline = Date.now() + 45000;
  while (Date.now() < deadline) {
    if (session.failure) throw session.failure;
    assert.equal(session.exit, null, `${name}: application exited during startup`);
    let targets;
    try { targets = await (await fetch(`http://127.0.0.1:${port}/json/list`, { signal: AbortSignal.timeout(1000) })).json(); }
    catch { await delay(200); continue; }
    const page = targets.find(target => target.type === "page" && target.url.startsWith("http://tauri.localhost"));
    if (page) { session.cdp = await CdpClient.connect(page.webSocketDebuggerUrl); break; }
    await delay(200);
  }
  assert(session.cdp, `${name}: packaged WebView did not start`);
  while (Date.now() < deadline) {
    if (await run(session, "!!document.querySelector('input[autocomplete=username]')")) break;
    await delay(100);
  }
  assert(await run(session, "!!document.querySelector('input[autocomplete=username]')"), "React login did not render");
  session.address = await run(session, "window.__TAURI_INTERNALS__.invoke('get_desktop_runtime_context').then(context => context.apiBaseUrl)");
  session.before = await processes(session);
  assert(session.before.some(entry => entry.Name === "msedgewebview2.exe"), "No owned WebView2 processes observed");
  await captureScreenshot(session.cdp, path.join(output, `${name}.png`));
  return session;
}

async function portIsClosed(address) {
  const url = new URL(address);
  return new Promise(resolve => {
    const socket = net.connect({ host: url.hostname, port: Number(url.port) });
    socket.setTimeout(1500);
    socket.once("connect", () => { socket.destroy(); resolve(false); });
    socket.once("error", () => resolve(true));
    socket.once("timeout", () => { socket.destroy(); resolve(false); });
  });
}

async function close(session, mode) {
  const started = Date.now();
  if (mode === "crash") {
    // Kill only the host to verify WebView2's host-disconnection behavior, not its process tree.
    session.child.kill();
  } else {
    const expression = mode === "repeat"
      ? "for(let i=0;i<12;i++) void window.__TAURI_INTERNALS__.invoke('request_app_exit');"
      : "void window.__TAURI_INTERNALS__.invoke('plugin:event|emit', {event:'exportdoc://exit-requested',payload:null});";
    await run(session, `setTimeout(() => { ${expression} }, 100); true`);
  }
  while (!session.exit && Date.now() - started < 50000) await delay(100);
  assert(session.exit, `${session.name}: host did not exit within its shutdown deadline`);
  if (mode !== "crash") assert.equal(session.exit.code, 0, "Graceful exit must succeed");
  let remaining = await processes(session);
  const cleanupDeadline = Date.now() + 15000;
  while (remaining.length && Date.now() < cleanupDeadline) {
    await delay(300);
    remaining = await processes(session);
  }
  const result = { name: session.name, mode, before: session.before, remaining, exit: session.exit, elapsedMs: Date.now() - started };
  results.push(result);
  fs.writeFileSync(path.join(output, "result.json"), JSON.stringify(results, null, 2));
  assert.equal(remaining.length, 0, `${session.name}: owned processes survived shutdown`);
  assert(await portIsClosed(session.address), "Rust HTTP listener survived shutdown");
  console.log(`${session.name}: ${mode}, ${session.before.filter(entry => entry.Name === "msedgewebview2.exe").length} WebView2 processes -> 0; HTTP port closed`);
}

try {
  const sibling = await start("independent-window");
  const dataRoot = path.join(output, "restart-data");
  for (const [index, mode] of ["normal", "repeat", "normal", "crash"].entries()) {
    const session = await start(`cycle-${index + 1}`, dataRoot);
    await close(session, mode);
    assert.equal(sibling.exit, null, "Closing a session terminated an independent application");
    assert.equal(await run(sibling, "document.readyState"), "complete");
  }
  await close(sibling, "normal");
  console.log(`Desktop shutdown evidence: ${output}`);
} finally {
  for (const session of sessions) {
    session.cdp?.socket.close();
    if (!session.exit) await stopProcessTree(session.child);
  }
}
