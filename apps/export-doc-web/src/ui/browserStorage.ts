export type BrowserStorageArea = "local" | "session";

export function readStoredJson<T>(storageKey: string, area: BrowserStorageArea = "local"): T | null {
  const storage = resolveBrowserStorage(area);
  if (!storage) {
    return null;
  }

  try {
    const rawValue = storage.getItem(storageKey);
    return rawValue ? (JSON.parse(rawValue) as T) : null;
  } catch {
    return null;
  }
}

export function readStoredJsonObject(
  storageKey: string,
  area: BrowserStorageArea = "local",
): Record<string, unknown> {
  const parsed = readStoredJson<unknown>(storageKey, area);
  return parsed && typeof parsed === "object" && !Array.isArray(parsed)
    ? (parsed as Record<string, unknown>)
    : {};
}

export function writeStoredJson(
  storageKey: string,
  value: unknown,
  area: BrowserStorageArea = "local",
) {
  const storage = resolveBrowserStorage(area);
  if (!storage) {
    return false;
  }

  try {
    storage.setItem(storageKey, JSON.stringify(value));
    return true;
  } catch {
    return false;
  }
}

export function removeStoredValue(storageKey: string, area: BrowserStorageArea = "local") {
  const storage = resolveBrowserStorage(area);
  if (!storage) {
    return;
  }

  try {
    storage.removeItem(storageKey);
  } catch {
    // Storage is an optional browser capability; business workflows must keep working without it.
  }
}

function resolveBrowserStorage(area: BrowserStorageArea): Storage | null {
  if (typeof window === "undefined") {
    return null;
  }

  try {
    return area === "session" ? window.sessionStorage : window.localStorage;
  } catch {
    return null;
  }
}
