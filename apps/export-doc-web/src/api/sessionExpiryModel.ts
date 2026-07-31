export const maximumBrowserTimeoutMs = 2_147_000_000;
export const sessionExpiryWarningLeadMs = 5 * 60 * 1000;

export function calculateSessionExpiryDelay(expiresAt: string, now = Date.now()) {
  const expiresAtMs = new Date(expiresAt).getTime();
  if (!Number.isFinite(expiresAtMs)) {
    return null;
  }

  return Math.min(Math.max(expiresAtMs - now, 0), maximumBrowserTimeoutMs);
}

export function calculateSessionWarningDelay(
  expiresAt: string,
  now = Date.now(),
  warningLeadMs = sessionExpiryWarningLeadMs,
) {
  const expiryDelay = calculateSessionExpiryDelay(expiresAt, now);
  if (expiryDelay === null) {
    return null;
  }

  return Math.min(Math.max(expiryDelay - Math.max(warningLeadMs, 0), 0), maximumBrowserTimeoutMs);
}
