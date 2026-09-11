export type RouteQueryPatch = Record<string, string | number | boolean | null | undefined>;

export function patchRouteQuery(search: string, patch: RouteQueryPatch) {
  const params = new URLSearchParams(search);
  for (const [key, value] of Object.entries(patch)) {
    if (value === null || value === undefined || value === "" || value === false) params.delete(key);
    else params.set(key, String(value));
  }
  const result = params.toString();
  return result ? `?${result}` : "";
}

export function readRouteId(value: string | null): number | null {
  if (!value || !/^[1-9]\d*$/.test(value)) return null;
  const id = Number(value);
  return Number.isSafeInteger(id) && id <= 2147483647 ? id : null;
}

export function readRouteChoice<T extends string>(value: string | null, choices: readonly T[], fallback: T): T {
  return choices.find((choice) => choice === value) ?? fallback;
}
