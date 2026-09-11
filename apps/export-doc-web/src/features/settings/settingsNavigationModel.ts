import type { SettingsCategoryKey } from "./settingsCategoryCatalog.ts";

function readSettingsSection(search: string) {
  return new URLSearchParams(search).get("section")?.trim();
}

export function readSettingsCategoryFromSearch(
  search: string,
  availableCategories?: readonly SettingsCategoryKey[],
): SettingsCategoryKey {
  let category: SettingsCategoryKey;
  switch (readSettingsSection(search)) {
    case "documents":
    case "documentDefaults":
    case "documentFields":
      category = "documents";
      break;
    case "excelImport":
      category = "excel-import";
      break;
    case "exchangeRate":
    case "currency":
    case "currencies":
      category = "exchange-rate";
      break;
    case "email":
      category = "communication";
      break;
    case "webDav":
    case "backup":
    case "backupPolicy":
    case "postgresql":
      category = "backup";
      break;
    case "singleWindow":
    case "ai":
      category = "single-window";
      break;
    case "maintenance":
    case "diagnostics":
    case "logs":
    case "ownership":
    case "support":
    case "validation":
      category = "maintenance";
      break;
    case "updater":
      category = "runtime";
      break;
    default:
      category = "runtime";
      break;
  }

  return !availableCategories || availableCategories.includes(category) ? category : "runtime";
}

export function readSettingsPanelLabelFromSearch(search: string) {
  switch (readSettingsSection(search)) {
    case "documents":
    case "documentDefaults":
      return "发票录入默认值";
    case "documentFields":
      return "单据字段名称";
    case "excelImport":
      return "Excel 导入方案";
    case "exchangeRate":
    case "currency":
    case "currencies":
      return "汇率与币制";
    case "email":
      return "邮件设置";
    case "webDav":
      return "WebDAV 云备份";
    case "backupPolicy":
      return "备份计划与保留";
    case "backup":
      return "数据备份与还原";
    case "singleWindow":
      return "单一窗口默认值";
    case "ai":
      return "AI 设置";
    case "postgresql":
      return "PostgreSQL 团队库维护";
    case "logs":
      return "日志管理";
    case "diagnostics":
      return "运行诊断";
    case "support":
      return "问题诊断包";
    case "ownership":
      return "数据归属改派";
    case "database":
      return "数据库连接";
    case "system":
      return "常规与目录";
    case "updater":
      return "软件更新";
    default:
      return null;
  }
}

export const documentFieldGroups = [
  { value: "invoice", label: "发票表头" },
  { value: "item", label: "商品明细" },
  { value: "payment", label: "付款报销" },
] as const;
export type DocumentFieldGroup = typeof documentFieldGroups[number]["value"];

export function readDocumentFieldGroup(search: string): DocumentFieldGroup {
  const group = new URLSearchParams(search).get("group");
  return documentFieldGroups.find((item) => item.value === group)?.value ?? "invoice";
}
