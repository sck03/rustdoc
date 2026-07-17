import { logFrontendError } from "./desktopBridge.ts";

let installed = false;

export function installFrontendErrorLogger() {
  if (installed || typeof window === "undefined") {
    return;
  }

  installed = true;
  window.addEventListener("error", (event) => {
    void writeFrontendError({
      message: event.message || "Frontend window error",
      source: event.filename ? `${event.filename}:${event.lineno}:${event.colno}` : "window.error",
      stack: event.error instanceof Error ? event.error.stack : "",
    });
  });

  window.addEventListener("unhandledrejection", (event) => {
    const reason = event.reason;
    void writeFrontendError({
      message: reason instanceof Error ? reason.message : stringifyReason(reason),
      source: "window.unhandledrejection",
      stack: reason instanceof Error ? reason.stack : "",
    });
  });
}

export function reportFrontendError(message: string, source: string, stack?: string) {
  return writeFrontendError({ message, source, stack });
}

async function writeFrontendError(payload: {
  message: string;
  source: string;
  stack?: string;
}) {
  try {
    await logFrontendError({
      ...payload,
      url: window.location.href,
    });
  } catch {
    // Error logging is best-effort and must not create recursive failures.
  }
}

function stringifyReason(reason: unknown) {
  if (reason == null) {
    return "Unhandled promise rejection";
  }

  if (typeof reason === "string") {
    return reason;
  }

  try {
    return JSON.stringify(reason);
  } catch {
    return String(reason);
  }
}
