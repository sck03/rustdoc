import { Cloud, Coins, Database, FileCog, FileSpreadsheet, Network, Wrench, type LucideIcon } from "lucide-react";

export type SettingsCategoryKey = "runtime" | "document-templates" | "excel-import" | "exchange-rate" | "communication" | "single-window" | "maintenance";
export type SettingsCategoryConfig = {
  key: SettingsCategoryKey;
  label: string;
  icon: LucideIcon;
  requiresDocumentWorkspace?: boolean;
};

export type SettingsCategoryCapabilities = {
  canUseDocumentWorkspace: boolean;
};

export const settingsCategories: SettingsCategoryConfig[] = [
  { key: "runtime", label: "运行与数据库", icon: Database },
  { key: "document-templates", label: "模板设置", icon: FileCog, requiresDocumentWorkspace: true },
  { key: "excel-import", label: "Excel 导入", icon: FileSpreadsheet, requiresDocumentWorkspace: true },
  { key: "exchange-rate", label: "汇率与币制", icon: Coins },
  { key: "communication", label: "邮件与备份", icon: Cloud },
  { key: "single-window", label: "AI 与单一窗口", icon: Network, requiresDocumentWorkspace: true },
  { key: "maintenance", label: "维护工具", icon: Wrench },
];

export function filterSettingsCategories(capabilities: SettingsCategoryCapabilities) {
  return settingsCategories.filter((category) => {
    if (category.requiresDocumentWorkspace && !capabilities.canUseDocumentWorkspace) return false;
    return true;
  });
}
