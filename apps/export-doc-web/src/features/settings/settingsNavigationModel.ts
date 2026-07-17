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
    case "batchExport":
    case "documentTemplates":
    case "paymentTemplates":
    case "documents":
      category = "document-templates";
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
    case "webDav":
    case "backup":
      category = "communication";
      break;
    case "singleWindow":
    case "ai":
      category = "single-window";
      break;
    case "maintenance":
    case "diagnostics":
    case "postgresql":
    case "ownership":
    case "support":
    case "validation":
      category = "maintenance";
      break;
    default:
      category = "runtime";
      break;
  }

  return !availableCategories || availableCategories.includes(category) ? category : "runtime";
}

export function readSettingsPanelLabelFromSearch(search: string) {
  switch (readSettingsSection(search)) {
    case "batchExport":
    case "documentTemplates":
    case "documents":
      return "单证模板设置";
    case "paymentTemplates":
      return "付款/报销模板设置";
    case "excelImport":
      return "Excel 导入方案";
    case "exchangeRate":
    case "currency":
    case "currencies":
      return "汇率与币制";
    case "email":
    case "webDav":
      return "邮件与备份";
    case "backup":
      return "数据备份与还原";
    case "singleWindow":
      return "单一窗口默认值";
    case "ai":
      return "AI 设置";
    case "postgresql":
      return "PostgreSQL";
    case "diagnostics":
      return "运行诊断";
    case "support":
      return "问题诊断包";
    case "ownership":
      return "数据归属改派";
    case "database":
    case "system":
      return "系统与数据库";
    default:
      return null;
  }
}
