export type SettingsRecord = Record<string, unknown>;

export function readNumberValue(settings: SettingsRecord, path: string[]) {
  const value = readNestedValue(settings, path);
  if (typeof value === "number") return value;
  if (typeof value === "string") { const next = Number(value); return Number.isFinite(next) ? next : 0; }
  return 0;
}

export function readBoolean(settings: SettingsRecord, path: string[]) { return readNestedValue(settings, path) === true; }

export function readNestedValue(settings: SettingsRecord, path: string[]) {
  let current: unknown = settings;
  for (const key of path) { if (!isRecord(current)) return undefined; current = current[key]; }
  return current;
}

export function setNestedValue(settings: SettingsRecord, path: string[], value: unknown) {
  let current = settings;
  for (const key of path.slice(0, -1)) { if (!isRecord(current[key])) current[key] = {}; current = current[key] as SettingsRecord; }
  current[path[path.length - 1]] = value;
}

export function cloneSettings(settings: SettingsRecord) { return JSON.parse(JSON.stringify(settings)) as SettingsRecord; }
export function isRecord(value: unknown): value is SettingsRecord { return Boolean(value) && typeof value === "object" && !Array.isArray(value); }

export function normalizeSettingText(value: string) { return value.trim().toUpperCase(); }

export function readStringArray(settings: SettingsRecord, path: string[]) {
  const value = readNestedValue(settings, path);
  if (Array.isArray(value)) return value.map((item) => item == null ? "" : String(item).trim()).filter(Boolean);
  return typeof value === "string" ? parseStringArray(value) : [];
}

export function parseStringArray(value: string) {
  return value.split(/[,\n，、]/).map((item) => item.trim()).filter(Boolean);
}

export function normalizeCurrencyList(currencies: string[]) {
  const normalized: string[] = [];
  const seen = new Set<string>();
  for (const currency of currencies) {
    const value = (currency ?? "").trim();
    if (!value || seen.has(value)) continue;
    normalized.push(value); seen.add(value);
  }
  return normalized;
}

export function readRecordValue(record: SettingsRecord, ...keys: string[]) {
  for (const key of keys) if (Object.prototype.hasOwnProperty.call(record, key)) return record[key];
  return undefined;
}

export function readRecordString(record: SettingsRecord, ...keys: string[]) {
  const value = readRecordValue(record, ...keys);
  return typeof value === "string" ? value.trim() : value == null ? "" : String(value).trim();
}

export function readFiniteNumber(value: unknown, fallback: number) {
  const parsed = typeof value === "number" ? value : typeof value === "string" ? Number(value) : Number.NaN;
  return Number.isFinite(parsed) ? parsed : fallback;
}

export function toPascalCase(value: string) { return value ? `${value[0].toUpperCase()}${value.slice(1)}` : value; }
