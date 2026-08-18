const maximumBrowserTimerDelayMs = 2_147_000_000;
const refreshAfterBoundaryDelayMs = 1_000;

export function calculateBusinessDateRefreshDelay(
  validUntilUtc: string | null | undefined,
  nowMs = Date.now(),
): number | null {
  if (!validUntilUtc) return null;

  const validUntilMs = Date.parse(validUntilUtc);
  if (!Number.isFinite(validUntilMs)) return 0;

  return Math.min(
    maximumBrowserTimerDelayMs,
    Math.max(0, validUntilMs - nowMs + refreshAfterBoundaryDelayMs),
  );
}
