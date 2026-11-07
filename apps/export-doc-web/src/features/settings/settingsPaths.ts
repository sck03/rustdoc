import type { AppSettings } from "../../api/index.ts";

export function readDefaultExportDirectory(settings: AppSettings | undefined) {
  return settings?.system.defaultExportDirectory.trim() ?? "";
}
