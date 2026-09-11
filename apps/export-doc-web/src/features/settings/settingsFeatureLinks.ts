import { settingsCategories } from "./settingsCategoryCatalog.ts";

const sections = [
  ["runtime", "system", "常规与目录", "运行参数 数据目录"],
  ["runtime", "database", "数据库连接", "SQLite PostgreSQL"],
  ["documents", "documentDefaults", "发票录入默认值", "空白行 备用列 显示列"],
  ["documents", "documentFields", "单据字段名称", "发票表头 商品明细 付款 备用字段"],
  ["documents", "singleWindow", "申报默认值", "单一窗口 申报员 签证机构 领证"],
  ["excel-import", "excelImport", "Excel 导入方案", "出口商 映射"],
  ["exchange-rate", "exchangeRate", "汇率与币制", "币种 汇率"],
  ["communication", "email", "邮件设置", "SMTP 发件人 收件人规则"],
  ["backup", "backupPolicy", "备份计划与保留", "自动备份 定时"],
  ["backup", "webDav", "WebDAV 云备份", "网盘 远程备份"],
  ["backup", "backup", "数据备份与还原", "恢复"],
  ["backup", "postgresql", "PostgreSQL 团队库维护", "完整迁移"],
  ["ai", "ai", "AI 服务", "模型 API Key 提示词"],
  ["maintenance", "logs", "日志管理", "审计留存 运行日志 清理"],
  ["maintenance", "diagnostics", "运行诊断", "依赖 浏览器"],
  ["maintenance", "ownership", "数据归属改派", "人员 公司 部门 移交"],
  ["maintenance", "invoice-cleanup", "发票清理", "作废 单据清理"],
  ["maintenance", "support", "问题诊断包", "支持 故障"],
] as const;

export const settingsFeatureLinks = sections.map(([categoryKey, section, label, keywords]) => {
  const category = settingsCategories.find((item) => item.key === categoryKey);
  return {
    label, keywords, to: `/settings?section=${section}`, search: `section=${section}`,
    description: `${category?.label ?? label} / ${label}`,
    ...(category?.requiresDocumentWorkspace || section === "invoice-cleanup" ? { workspace: "document" as const } : {}),
    ...(section === "ownership" ? { requiresSystemAdministration: true } : {}),
  };
});
