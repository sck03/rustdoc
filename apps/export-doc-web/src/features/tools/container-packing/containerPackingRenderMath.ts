const maximumGridSegments = 18;

export function clampContainerPackingGridSegments(value: number) {
  return Number.isFinite(value)
    ? Math.min(Math.max(Math.trunc(value), 1), maximumGridSegments)
    : 1;
}

export function clampNumber(value: number, min: number, max: number) {
  return Number.isFinite(value) ? Math.min(Math.max(value, min), max) : min;
}
