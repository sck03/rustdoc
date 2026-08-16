import { spawn } from "node:child_process";

export function spawnProcessTree(command, args, options = {}) {
  return spawn(command, args, {
    ...options,
    detached: process.platform !== "win32",
  });
}

export async function stopProcessTree(child, timeoutMs = 5000) {
  if (!child?.pid) return true;

  if (process.platform === "win32") {
    if (!isProcessAlive(child.pid)) return true;
    await runCleanupProcess("taskkill.exe", ["/pid", String(child.pid), "/T", "/F"], timeoutMs);
    if (isProcessAlive(child.pid)) {
      const command =
        `$process = [Diagnostics.Process]::GetProcessById(${child.pid}); ` +
        "$process.Kill($true); $process.WaitForExit(5000) | Out-Null";
      await runCleanupProcess("powershell.exe", [
        "-NoLogo",
        "-NoProfile",
        "-NonInteractive",
        "-Command",
        command,
      ], timeoutMs);
    }
    if (isProcessAlive(child.pid)) {
      child.kill("SIGKILL");
    }
    await waitForChildExit(child, timeoutMs);
    return !isProcessAlive(child.pid);
  }

  if (!isProcessGroupAlive(child.pid)) return true;
  signalProcessGroup(child.pid, "SIGTERM");
  if (!(await waitForProcessGroupExit(child.pid, timeoutMs))) {
    signalProcessGroup(child.pid, "SIGKILL");
    await waitForProcessGroupExit(child.pid, Math.min(timeoutMs, 2000));
  }
  await waitForChildExit(child, Math.min(timeoutMs, 1000));
  return !isProcessGroupAlive(child.pid);
}

function runCleanupProcess(command, args, timeoutMs) {
  return new Promise((resolve) => {
    const killer = spawn(command, args, {
      stdio: "ignore",
      windowsHide: true,
    });
    killer.unref();
    let settled = false;
    const timer = setTimeout(() => {
      if (settled) return;
      settled = true;
      killer.kill("SIGKILL");
      resolve(false);
    }, timeoutMs);
    const finish = (success) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      resolve(success);
    };
    killer.once("exit", (code) => finish(code === 0));
    killer.once("error", () => finish(false));
  });
}

function signalProcessGroup(pid, signal) {
  try {
    process.kill(-pid, signal);
  } catch (error) {
    if (error?.code !== "ESRCH") throw error;
  }
}

function isProcessGroupAlive(pid) {
  try {
    process.kill(-pid, 0);
    return true;
  } catch (error) {
    if (error?.code === "ESRCH") return false;
    if (error?.code === "EPERM") return true;
    throw error;
  }
}

function isProcessAlive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    if (error?.code === "ESRCH") return false;
    if (error?.code === "EPERM") return true;
    throw error;
  }
}

async function waitForProcessGroupExit(pid, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (!isProcessGroupAlive(pid)) return true;
    await delay(50);
  }
  return !isProcessGroupAlive(pid);
}

function waitForChildExit(child, timeoutMs) {
  if (child.exitCode !== null || child.signalCode !== null) return Promise.resolve(true);
  return new Promise((resolve) => {
    const timer = setTimeout(() => {
      child.off("exit", onExit);
      resolve(false);
    }, timeoutMs);
    function onExit() {
      clearTimeout(timer);
      resolve(true);
    }
    child.once("exit", onExit);
  });
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
