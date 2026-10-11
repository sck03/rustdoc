/** Round boundaries, so editable percentages always cover the whole table. */
export function normalizeColumnPercentages<T extends { widthPercent: number }>(columns: T[]): T[] {
  const total = columns.reduce((sum, column) => sum + Math.max(1, column.widthPercent), 0);
  const minimumUnits = Math.min(10, Math.floor(1000 / columns.length));
  let consumed = 0;
  let previousBoundary = 0;
  return columns.map((column, index) => {
    consumed += Math.max(1, column.widthPercent);
    const boundary = Math.min(1000 - (columns.length - index - 1) * minimumUnits,
      Math.max(previousBoundary + minimumUnits, Math.round(consumed / total * 1000)));
    const widthPercent = (boundary - previousBoundary) / 10;
    previousBoundary = boundary;
    return widthPercent === column.widthPercent ? column : { ...column, widthPercent };
  });
}
