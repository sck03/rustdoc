import type { ApiUserDto } from "../api/index.ts";
import { calculateSessionExpiryDelay } from "../api/sessionExpiryModel.ts";
import { readStoredJson, removeStoredValue, writeStoredJson } from "../ui/browserStorage.ts";

const sessionStorageKey = "exportdocmanager.web.session";

export type WebSessionState = {
  accessToken: string;
  expiresAt: string;
  apiBaseUrl: string;
  user: ApiUserDto;
};

export function readStoredSession(): WebSessionState | null {
  try {
    const session = readStoredJson<WebSessionState>(sessionStorageKey, "session");
    removeStoredValue(sessionStorageKey, "local");
    if (!session) {
      return null;
    }

    const expiryDelay = calculateSessionExpiryDelay(session.expiresAt);
    if (expiryDelay === null || expiryDelay === 0) {
      clearStoredSession();
      return null;
    }

    return session;
  } catch {
    return null;
  }
}

export function writeStoredSession(session: WebSessionState) {
  removeStoredValue(sessionStorageKey, "local");
  writeStoredJson(sessionStorageKey, session, "session");
}

export function clearStoredSession() {
  removeStoredValue(sessionStorageKey, "session");
  removeStoredValue(sessionStorageKey, "local");
}
