import os from "node:os";

const legacyWindowsMaximumBuild = 19040;

export function buildChromiumSandboxArguments(runtime = detectChromiumRuntime()) {
  const argumentsList = shouldDisableChromiumSandbox(runtime) ? ["--no-sandbox"] : [];
  if (requiresLegacyWindowsCompatibility(runtime)) {
    argumentsList.push("--in-process-gpu");
  }
  return argumentsList;
}

export function requiresLegacyWindowsCompatibility(runtime = detectChromiumRuntime()) {
  return runtime.platform === "win32" &&
    Number.isInteger(runtime.windowsBuild) &&
    runtime.windowsBuild >= 0 &&
    runtime.windowsBuild <= legacyWindowsMaximumBuild;
}

export function shouldDisableChromiumSandbox(runtime = detectChromiumRuntime()) {
  const configured = runtime.noSandboxSetting;
  if (configured !== undefined && configured !== null && String(configured).trim() !== "") {
    return parseBooleanSetting(configured);
  }

  if (runtime.platform === "linux" && (runtime.isCi || runtime.isRoot)) {
    return true;
  }

  return runtime.platform === "win32" &&
    Number.isInteger(runtime.windowsBuild) &&
    runtime.windowsBuild >= 0 &&
    runtime.windowsBuild <= legacyWindowsMaximumBuild;
}

export function detectChromiumRuntime() {
  return {
    platform: process.platform,
    isCi: Boolean(process.env.CI),
    isRoot: typeof process.getuid === "function" && process.getuid() === 0,
    windowsBuild: parseWindowsBuild(os.release()),
    noSandboxSetting: process.env.EXPORTDOCMANAGER_CHROMIUM_NO_SANDBOX,
  };
}

export function parseWindowsBuild(release) {
  if (typeof release !== "string") return null;
  const parts = release.split(".");
  if (parts.length < 3) return null;
  const build = Number.parseInt(parts[2], 10);
  return Number.isInteger(build) ? build : null;
}

function parseBooleanSetting(value) {
  const normalized = String(value).trim().toLowerCase();
  if (normalized === "1" || normalized === "true") return true;
  if (normalized === "0" || normalized === "false") return false;
  throw new Error(
    "EXPORTDOCMANAGER_CHROMIUM_NO_SANDBOX must be 0, 1, false, or true.",
  );
}
