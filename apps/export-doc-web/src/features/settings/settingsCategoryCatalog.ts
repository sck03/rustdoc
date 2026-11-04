import { Archive, Coins, Database, FileSliders, FileSpreadsheet, Mail, Network, Wrench, type LucideIcon } from "lucide-react";

export type SettingsCategoryKey = "runtime" | "documents" | "excel-import" | "exchange-rate" | "communication" | "backup" | "ai" | "maintenance";
export type SettingsCategoryConfig = {
  key: SettingsCategoryKey;
  label: string;
  section: string;
  icon: LucideIcon;
  requiresDocumentWorkspace?: boolean;
};

export type SettingsCategoryCapabilities = {
  canUseDocumentWorkspace: boolean;
};

export const settingsCategories: SettingsCategoryConfig[] = [
  { key: "runtime", label: "运行与数据库", section: "system", icon: Database },
  { key: "documents", label: "单据设置", section: "documentDefaults", icon: FileSliders, requiresDocumentWorkspace: true },
  { key: "excel-import", label: "Excel 导入", section: "excelImport", icon: FileSpreadsheet, requiresDocumentWorkspace: true },
  { key: "exchange-rate", label: "汇率与币制", section: "exchangeRate", icon: Coins },
  { key: "communication", label: "邮件设置", section: "email", icon: Mail },
  { key: "ai", label: "AI 服务", section: "ai", icon: Network, requiresDocumentWorkspace: true },
];
export const allSettingsCategories: SettingsCategoryConfig[] = [...settingsCategories,
  { key: "backup", label: "备份与恢复", section: "plan", icon: Archive },
  { key: "maintenance", label: "系统维护", section: "diagnostics", icon: Wrench },
];

export function filterSettingsCategories(capabilities: SettingsCategoryCapabilities) {
  return settingsCategories.filter((category) => {
    if (category.requiresDocumentWorkspace && !capabilities.canUseDocumentWorkspace) return false;
    return true;
  });
}
