import type { WebSessionState } from "./webSessionStorage.ts";

const channelName = "exportdocmanager.session.v1";

export type SessionChannelMessage =
  | { type: "session-updated"; session: WebSessionState }
  | { type: "session-cleared"; apiBaseUrl: string }
  | { type: "session-expired"; apiBaseUrl: string; reason: string };

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
      (candidate.type !== "session-expired" || typeof candidate.reason === "string");
  }
  if (candidate.type !== "session-updated" || !candidate.session || typeof candidate.session !== "object") {
    return false;
  }

  const session = candidate.session as Record<string, unknown>;
  return typeof session.accessToken === "string" &&
    typeof session.expiresAt === "string" &&
    typeof session.apiBaseUrl === "string" &&
    Boolean(session.user && typeof session.user === "object");
}
