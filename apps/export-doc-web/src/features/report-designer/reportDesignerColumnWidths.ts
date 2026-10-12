/** Preserve valid precision; normalize cumulative boundaries without widening the table. */
export function normalizeColumnPercentages<T extends { widthPercent: number }>(columns: T[]): T[] {
  const weights = columns.map(column => Number.isFinite(column.widthPercent) ? Math.max(1, column.widthPercent) : 1);
  const total = weights.reduce((sum, width) => sum + width, 0);
  if (columns.every((column, index) => column.widthPercent === weights[index]) && Math.abs(total - 100) < 1e-8) return columns;
  const minimum = Math.min(1, 100 / columns.length);
  let consumed = 0;
  let previousBoundary = 0;
  return columns.map((column, index) => {
    consumed += weights[index];
    const boundary = Math.min(100 - (columns.length - index - 1) * minimum,
      Math.max(previousBoundary + minimum, consumed / total * 100));
    const widthPercent = boundary - previousBoundary;
    previousBoundary = boundary;
    return widthPercent === column.widthPercent ? column : { ...column, widthPercent };
  });
}
