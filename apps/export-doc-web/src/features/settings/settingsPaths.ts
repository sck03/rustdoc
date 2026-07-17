export function readDefaultExportDirectory(settings: unknown) {
  const system = readRecordValue(settings, "system", "System");
  const value = readRecordValue(system, "defaultExportDirectory", "DefaultExportDirectory");
  return typeof value === "string" ? value.trim() : "";
}

function readRecordValue(source: unknown, ...names: string[]) {
  if (!isRecord(source)) {
    return undefined;
  }

  for (const name of names) {
    if (Object.prototype.hasOwnProperty.call(source, name)) {
      return source[name];
    }
  }

  return undefined;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}
