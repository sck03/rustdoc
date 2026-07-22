export const maximumBrowserTimeoutMs = 2_147_000_000;

export function calculateSessionExpiryDelay(expiresAt: string, now = Date.now()) {
  const expiresAtMs = new Date(expiresAt).getTime();
  if (!Number.isFinite(expiresAtMs)) {
    return null;
  }

  return Math.min(Math.max(expiresAtMs - now, 0), maximumBrowserTimeoutMs);
}
