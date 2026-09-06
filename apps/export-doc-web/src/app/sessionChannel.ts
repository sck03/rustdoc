import type { WebSessionState } from "./webSessionStorage.ts";

const channelName = "exportdocmanager.session.v1";

export type SessionChannelMessage =
  | { type: "session-updated"; session: WebSessionState; previousAccessToken: string | null }
  | { type: "session-cleared"; apiBaseUrl: string; accessToken: string }
  | { type: "session-expired"; apiBaseUrl: string; accessToken: string; reason: string };

export function isCurrentSession(current: WebSessionState | null, expected: WebSessionState | null): boolean {
  return current === null || expected === null
    ? current === expected
    : current.apiBaseUrl === expected.apiBaseUrl && current.accessToken === expected.accessToken;
}

export function shouldAcceptSessionUpdate(
  current: WebSessionState | null,
  event: Extract<SessionChannelMessage, { type: "session-updated" }>,
): boolean {
  if (!current) return event.previousAccessToken === null;
  if (current.apiBaseUrl !== event.session.apiBaseUrl || current.user.id !== event.session.user.id) return false;
  return event.previousAccessToken === current.accessToken ||
    (event.previousAccessToken === null && Date.parse(event.session.expiresAt) > Date.parse(current.expiresAt));
}

export type SessionChannel = {
  post: (message: SessionChannelMessage) => void;
  close: () => void;
};

export function openSessionChannel(
  onMessage: (message: SessionChannelMessage) => void,
): SessionChannel | null {
  if (typeof BroadcastChannel === "undefined") {
    return null;
  }

  const channel = new BroadcastChannel(channelName);
  const handleMessage = (event: MessageEvent<unknown>) => {
    if (!isSessionChannelMessage(event.data)) {
      return;
    }
    onMessage(event.data);
  };
  channel.addEventListener("message", handleMessage);

  return {
    post: (message) => channel.postMessage(message),
    close: () => {
      channel.removeEventListener("message", handleMessage);
      channel.close();
    },
  };
}

function isSessionChannelMessage(value: unknown): value is SessionChannelMessage {
  if (!value || typeof value !== "object") {
    return false;
  }

  const candidate = value as Record<string, unknown>;
  if (candidate.type === "session-cleared" || candidate.type === "session-expired") {
    return typeof candidate.apiBaseUrl === "string" &&
      typeof candidate.accessToken === "string" && candidate.accessToken.length > 0 &&
      (candidate.type !== "session-expired" || typeof candidate.reason === "string");
  }
  if (candidate.type !== "session-updated" || !candidate.session || typeof candidate.session !== "object") {
    return false;
  }

  const session = candidate.session as Record<string, unknown>;
  const user = session.user as Record<string, unknown> | undefined;
  return (candidate.previousAccessToken === null || typeof candidate.previousAccessToken === "string") &&
    typeof session.accessToken === "string" && session.accessToken.length > 0 &&
    typeof session.expiresAt === "string" && Number.isFinite(Date.parse(session.expiresAt)) &&
    typeof session.apiBaseUrl === "string" &&
    Boolean(user && typeof user === "object" && typeof user.id === "number" && user.id > 0 &&
      user.capabilities && typeof user.capabilities === "object");
}
