import { existsSync, rmSync } from "node:fs";
import { readdir, stat } from "node:fs/promises";
import path from "node:path";
import { delay } from "./chromium-cdp.mjs";

export function desktopAccessHeaders(options) {
  return options.desktopAccessToken
    ? { "X-ExportDocManager-Desktop-Token": options.desktopAccessToken }
    : {};
}

export function ensureTrailingSlash(value) {
  return value.endsWith("/") ? value : `${value}/`;
}

export function cloneJson(value) {
  return JSON.parse(JSON.stringify(value ?? {}));
}

export function redactDesktopAccessToken(value) {
  try {
    const url = new URL(value);
    if (url.searchParams.has("desktopAccessToken")) {
      url.searchParams.set("desktopAccessToken", "[redacted]");
    }

    return url.toString();
  } catch {
    return String(value).replace(/desktopAccessToken=[^&#]*/g, "desktopAccessToken=[redacted]");
  }
}

export function setRecordValueKeepingExistingCase(record, names, value) {
  if (!record || typeof record !== "object") {
    return;
  }

  const existingName = names.find((name) => Object.prototype.hasOwnProperty.call(record, name));
  record[existingName ?? names[0]] = value;
}

export async function readFileSize(filePath) {
  return (await stat(filePath)).size;
}

export async function collectFilesByExtension(rootPath, extension) {
  const normalizedExtension = String(extension || "").toLowerCase();
  const entries = await readdir(rootPath, { withFileTypes: true });
  const files = [];
  for (const entry of entries) {
    const entryPath = path.join(rootPath, entry.name);
    if (entry.isDirectory()) {
      files.push(...await collectFilesByExtension(entryPath, normalizedExtension));
      continue;
    }

    if (entry.isFile() && path.extname(entry.name).toLowerCase() === normalizedExtension) {
      files.push(entryPath);
    }
  }

  return files;
}

export function cleanupSmokeFile(filePath, rootPath) {
  assertSafeSmokePath(filePath, rootPath, "file");
  rmSync(filePath, { force: true });
  return !existsSync(filePath);
}

export function cleanupSmokeDirectory(directoryPath, rootPath) {
  assertSafeSmokePath(directoryPath, rootPath, "directory");
  rmSync(directoryPath, { recursive: true, force: true });
  return !existsSync(directoryPath);
}

function assertSafeSmokePath(candidatePath, rootPath, kind) {
  if (!candidatePath || !rootPath || !isPathInsideRoot(candidatePath, rootPath)) {
    throw new Error(
      `Refusing to remove smoke ${kind} outside its root. ${kind}Path=${candidatePath}; rootPath=${rootPath}`,
    );
  }
}

export function isPathInsideRoot(candidatePath, rootPath) {
  const rootKey = normalizePathForCompare(path.resolve(rootPath));
  const candidateKey = normalizePathForCompare(path.resolve(candidatePath));
  return Boolean(rootKey && candidateKey && (candidateKey === rootKey || candidateKey.startsWith(`${rootKey}/`)));
}

export function authorizedHeaders(options, accessToken, tokenType = "Bearer") {
  return {
    Authorization: `${tokenType || "Bearer"} ${accessToken}`,
    ...desktopAccessHeaders(options),
  };
}

export function authorizedJsonHeaders(options, accessToken, tokenType = "Bearer") {
  return {
    "Content-Type": "application/json",
    ...authorizedHeaders(options, accessToken, tokenType),
  };
}

export function buildBatchExportSettingsDeepLinkUrl(webUrl) {
  return buildSettingsSectionUrl(webUrl, "batchExport", "smokeBatchExportSettings");
}

export function buildDocumentEmailSettingsDeepLinkUrl(webUrl) {
  return buildSettingsSectionUrl(webUrl, "email", "smokeDocumentEmailSettings");
}

export function buildSettingsSectionUrl(webUrl, section, smokeParamName) {
  const url = new URL(webUrl);
  if (smokeParamName) {
    url.searchParams.set(smokeParamName, "1");
  }

  url.hash = `/settings?section=${encodeURIComponent(section)}`;
  return url.toString();
}

export function smokeFileNameFromPath(value) {
  return String(value ?? "").split(/[\\/]/).filter(Boolean).pop() || "";
}

export function normalizePathForCompare(value) {
  let text = String(value ?? "")
    .trim()
    .replace(/[\\/]+/g, "/")
    .replace(/\/+$/, "");

  if (/^[A-Za-z]:\//.test(text)) {
    text = text.toLocaleLowerCase();
  }

  return text;
}

export function includesText(text, expected) {
  return text.toLocaleLowerCase().includes(expected.toLocaleLowerCase());
}

export async function fetchJson(url) {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`GET ${url} failed with HTTP ${response.status}`);
  }

  return response.json();
}

export async function waitFor(probe, timeoutMs, timeoutMessage) {
  const deadline = Date.now() + timeoutMs;
  let lastError;

  while (Date.now() < deadline) {
    try {
      const result = await probe();
      if (result) {
        return result;
      }
    } catch (error) {
      lastError = error;
    }

    await delay(250);
  }

  const message = typeof timeoutMessage === "function" ? timeoutMessage() : timeoutMessage;
  if (lastError) {
    throw new Error(`${message}\nLast error: ${lastError.message}`);
  }

  throw new Error(message);
}
