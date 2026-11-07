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
    case "singleWindow":
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
    case "ai":
      category = "ai";
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
    case "documentDefaults":
      return "发票录入默认值";
    case "documentFields":
      return "单据字段名称";
    case "currency":
    case "currencies":
      return "汇率与币制";
    case "singleWindow":
      return "申报默认值";
    case "database":
      return "数据库连接";
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
