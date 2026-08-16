const redacted = "[REDACTED]";
const sensitiveKey = "(?:password|passwd|pwd|secret|api[-_]?key|token|credential|access[-_]?key|signing[-_]?key|encryption[-_]?key|connection[-_]?string)";
const bearerPattern = /\bBearer\s+[A-Za-z0-9._~+/=-]{8,}/giu;
const sensitiveAssignmentPattern = new RegExp(`(?<prefix>["']?${sensitiveKey}["']?\\s*(?:=|:)\\s*["']?)[^"'\\s,;}\\]]+`, "giu");
const emailPattern = /\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b/giu;
const httpUrlPattern = /https?:\/\/[^\s"'<>]+/giu;
const windowsUserPathPattern = /\b([A-Z]:\\Users)\\[^\\\s"'<>|]+/giu;
const unixHomePathPattern = /(?<![A-Za-z0-9_])(\/(?:Users|home))\/[^/\s"'<>]+/gu;

export type FrontendLogPayload = {
  message: string;
  source: string;
  stack?: string;
  url: string;
};

export function sanitizeFrontendLogPayload(payload: FrontendLogPayload): FrontendLogPayload {
  return {
    message: sanitizeFrontendLogText(payload.message),
    source: sanitizeFrontendLogText(payload.source),
    stack: sanitizeFrontendLogText(payload.stack ?? ""),
    url: sanitizeFrontendLogUrl(payload.url),
  };
}

export function sanitizeFrontendLogText(value: string) {
  return String(value ?? "")
    .replace(httpUrlPattern, sanitizeFrontendLogUrl)
    .replace(bearerPattern, `Bearer ${redacted}`)
    .replace(sensitiveAssignmentPattern, (...args) => `${args.at(-1)?.prefix ?? ""}${redacted}`)
    .replace(emailPattern, "[REDACTED_EMAIL]")
    .replace(windowsUserPathPattern, "$1\\[REDACTED]")
    .replace(unixHomePathPattern, "$1/[REDACTED]")
    .replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/gu, " ");
}

export function sanitizeFrontendLogUrl(value: string) {
  try {
    const url = new URL(value);
    if (url.protocol !== "http:" && url.protocol !== "https:") {
      return "[REDACTED_URL]";
    }

    url.username = "";
    url.password = "";
    url.search = "";
    url.hash = "";
    url.pathname = url.pathname
      .split("/")
      .map((segment) => segment.length >= 24 && /^[A-Za-z0-9._-]+$/u.test(segment) ? redacted : segment)
      .join("/");
    return url.toString();
  } catch {
    return "[REDACTED_URL]";
  }
}
