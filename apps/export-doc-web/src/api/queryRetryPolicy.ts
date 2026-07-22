import { ApiError } from "./index.ts";

const transientHttpStatuses = new Set([408, 425, 429, 500, 502, 503, 504]);

export function shouldRetryQueryFailure(failureCount: number, error: unknown) {
  if (failureCount >= 2) {
    return false;
  }

  if (error instanceof ApiError) {
    return transientHttpStatuses.has(error.status);
  }

  if (error instanceof DOMException && error.name === "AbortError") {
    return false;
  }

  return error instanceof TypeError;
}

export function queryRetryDelay(attemptIndex: number) {
  return Math.min(750 * 2 ** Math.max(0, attemptIndex), 3000);
}
