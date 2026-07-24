import { spawn } from "node:child_process";

export function spawnProcessTree(command, args, options = {}) {
  return spawn(command, args, {
    ...options,
    detached: process.platform !== "win32",
  });
}

export async function stopProcessTree(child, timeoutMs = 5000) {
  if (!child?.pid) return;

  if (process.platform === "win32") {
    if (child.exitCode !== null || child.signalCode !== null) return;
    await new Promise((resolve) => {
      const killer = spawn("taskkill.exe", ["/pid", String(child.pid), "/T", "/F"], {
        stdio: "ignore",
        windowsHide: true,
      });
      killer.once("exit", resolve);
      killer.once("error", resolve);
    });
    await waitForChildExit(child, timeoutMs);
    return;
  }

  if (!isProcessGroupAlive(child.pid)) return;
  signalProcessGroup(child.pid, "SIGTERM");
  if (!(await waitForProcessGroupExit(child.pid, timeoutMs))) {
    signalProcessGroup(child.pid, "SIGKILL");
    await waitForProcessGroupExit(child.pid, Math.min(timeoutMs, 2000));
  }
  await waitForChildExit(child, Math.min(timeoutMs, 1000));
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
