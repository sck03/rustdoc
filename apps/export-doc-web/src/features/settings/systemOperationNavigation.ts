export type BackupSection = "plan" | "replica" | "archives" | "recovery" | "migration";
export type MaintenanceSection = "diagnostics" | "logs" | "ownership" | "invoice-cleanup" | "support" | "jobs";
export const backupSections = [
  { key: "plan", label: "备份计划" }, { key: "replica", label: "异机副本" },
  { key: "archives", label: "备份文件" }, { key: "recovery", label: "恢复" }, { key: "migration", label: "整机迁移" },
] as const;
export const maintenanceSections = [
  { key: "diagnostics", label: "运行检查" }, { key: "logs", label: "日志管理" },
  { key: "ownership", label: "数据归属" }, { key: "invoice-cleanup", label: "发票清理" },
  { key: "support", label: "问题诊断包" }, { key: "jobs", label: "后台任务" },
] as const;
const legacyDestinations: Record<string, string> = {
  backupPolicy: "/system/backup?section=plan", backup: "/system/backup?section=plan",
  backupReplica: "/system/backup?section=replica", webDav: "/system/backup?section=replica",
  backupRecovery: "/system/backup?section=recovery", postgresql: "/system/backup?section=recovery",
  backupMigration: "/system/backup?section=migration",
  logs: "/system/maintenance?section=logs", ownership: "/system/maintenance?section=ownership",
  "invoice-cleanup": "/system/maintenance?section=invoice-cleanup",
  diagnostics: "/system/maintenance?section=diagnostics", validation: "/system/maintenance?section=diagnostics",
  support: "/system/maintenance?section=support", maintenance: "/system/maintenance",
};
export function settingsOperationDestination(search: string): string | null {
  const query = new URLSearchParams(search);
  const destination = legacyDestinations[query.get("section")?.trim() ?? ""];
  if (!destination) return null;
  const [pathname, parameters] = destination.split("?");
  const target = new URLSearchParams(parameters);
  for (const [key, value] of query) if (key !== "section") target.append(key, value);
  return pathname + (target.size ? `?${target}` : "");
}
export function operationSection(search: string, kind: "backup" | "maintenance") {
  const requested = new URLSearchParams(search).get("section");
  const sections = kind === "backup" ? backupSections : maintenanceSections;
  return sections.find(section => section.key === requested)?.key ?? sections[0].key;
}
