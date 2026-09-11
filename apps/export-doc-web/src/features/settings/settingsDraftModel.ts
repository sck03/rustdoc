import { settingsCategories, type SettingsCategoryKey } from "./settingsCategoryCatalog.ts";
import type { SettingsRecord } from "./settingsTypes.ts";

export function settingsCategoryForPath([root, field = ""]: readonly string[]): SettingsCategoryKey {
  if (root === "system") {
    if (["documentFieldLabels", "itemEntryBlankRowCount", "itemEntrySpareColumnCount"].includes(field)) return "documents";
    if (field === "defaultTemplateExporterNameCn") return "excel-import";
    if (field === "backupRetentionDays" || field.startsWith("postgreSqlAutoBackup")) return "backup";
    if (field === "auditLogRetentionDays" || field.startsWith("log")) return "maintenance";
    return "runtime";
  }
  if (root === "excelImport" || root === "excelImportSchemes") return "excel-import";
  if (root === "exchangeRate") return "exchange-rate";
  if (root === "email") return "communication";
  if (root === "webDav") return "backup";
  if (root === "singleWindow") return "documents";
  if (root === "ai") return "ai";
  return "runtime";
}

function equalValues(left: unknown, right: unknown): boolean {
  if (left === right) return true;
  if (!left || !right || typeof left !== "object" || typeof right !== "object") return false;
  if (Array.isArray(left) !== Array.isArray(right)) return false;
  const a = left as SettingsRecord, b = right as SettingsRecord;
  const keys = new Set([...Object.keys(a), ...Object.keys(b)]);
  return [...keys].every((key) => equalValues(a[key], b[key]));
}

export function changedSettingsCategories(baseline: SettingsRecord | null, draft: SettingsRecord | null) {
  if (!baseline || !draft) return [];
  const changed = new Set<SettingsCategoryKey>();
  for (const root of new Set([...Object.keys(baseline), ...Object.keys(draft)])) {
    if (root === "revision" || equalValues(baseline[root], draft[root])) continue;
    if (root === "system") {
      const before = (baseline[root] ?? {}) as SettingsRecord, after = (draft[root] ?? {}) as SettingsRecord;
      for (const field of new Set([...Object.keys(before), ...Object.keys(after)])) {
        if (!equalValues(before[field], after[field])) changed.add(settingsCategoryForPath([root, field]));
      }
    } else changed.add(settingsCategoryForPath([root]));
  }
  return settingsCategories.filter((category) => changed.has(category.key));
}

export function categoryHasSecrets(category: SettingsCategoryKey, databaseProvider?: string | null) {
  return category === "communication" || category === "backup" || category === "ai"
    || (category === "runtime" && databaseProvider === "PostgreSQL");
}
