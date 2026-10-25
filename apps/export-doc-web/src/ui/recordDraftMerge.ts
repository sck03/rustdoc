/** Accept server-normalized fields only when they were unchanged since submission. */
export function mergeSavedDraftFields<T extends object>(current: T, submitted: T, saved: T, fields: readonly (keyof T)[]): T {
  const merged = { ...saved };
  for (const field of fields) {
    if (!Object.is(current[field], submitted[field])) merged[field] = current[field];
  }
  return merged;
}
