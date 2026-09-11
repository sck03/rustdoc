import { Archive, Coins, Database, FileSliders, FileSpreadsheet, Mail, Network, Wrench, type LucideIcon } from "lucide-react";

export type SettingsCategoryKey = "runtime" | "documents" | "excel-import" | "exchange-rate" | "communication" | "backup" | "single-window" | "maintenance";
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
  { key: "documents", label: "单据设置", icon: FileSliders, requiresDocumentWorkspace: true },
  { key: "excel-import", label: "Excel 导入", icon: FileSpreadsheet, requiresDocumentWorkspace: true },
  { key: "exchange-rate", label: "汇率与币制", icon: Coins },
  { key: "communication", label: "邮件设置", icon: Mail },
  { key: "backup", label: "备份与恢复", icon: Archive },
  { key: "single-window", label: "AI 与单一窗口", icon: Network, requiresDocumentWorkspace: true },
  { key: "maintenance", label: "维护工具", icon: Wrench },
];

export function filterSettingsCategories(capabilities: SettingsCategoryCapabilities) {
  return settingsCategories.filter((category) => {
    if (category.requiresDocumentWorkspace && !capabilities.canUseDocumentWorkspace) return false;
    return true;
  });
}
