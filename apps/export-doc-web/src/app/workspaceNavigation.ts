import {
  CircleDollarSign,
  BookOpen,
  ContactRound,
  ClipboardList,
  CreditCard,
  Database,
  FileSpreadsheet,
  Factory,
  FileText,
  Info,
  KeyRound,
  LayoutDashboard,
  Mail,
  Network,
  PackageCheck,
  RefreshCw,
  ScanText,
  Search,
  Settings,
  ShieldCheck,
  ScrollText,
  UsersRound,
  type LucideIcon,
} from "lucide-react";

export type WorkspaceNavItem = {
  label: string;
  to: string;
  icon: LucideIcon;
  isActive: (pathname: string) => boolean;
  requiresAdmin?: boolean;
  desktopOnly?: boolean;
  requiresFullEdition?: boolean;
  workspace?: "document" | "sales";
  moduleKey?: string;
};

export type WorkspaceCapabilities = {
  canManageSettings?: boolean;
  canUseDocumentWorkspace?: boolean;
  canUseSalesWorkspace?: boolean;
  isDesktopRuntime?: boolean;
  productEdition?: unknown;
  enabledModules?: string[];
};

export type WorkspaceNavGroupConfig = {
  key: string;
  label: string;
  icon: LucideIcon;
  items: WorkspaceNavItem[];
};

export type WorkspaceContext = {
  section: string;
  title: string;
  description: string;
  icon: LucideIcon;
};

export const defaultExpandedWorkspaceNavGroups = ["workspace", "documents"];

export const workspaceNavGroups: WorkspaceNavGroupConfig[] = [
  {
    key: "workspace",
    label: "工作台",
    icon: LayoutDashboard,
    items: [{ label: "仪表盘", to: "/dashboard", icon: LayoutDashboard, isActive: isDashboardRoute, workspace: "document", moduleKey: "document.dashboard" }],
  },
  {
    key: "sales",
    label: "客户业务",
    icon: ContactRound,
    items: [
      { label: "销售概览", to: "/crm/dashboard", icon: LayoutDashboard, isActive: isCrmDashboardRoute, workspace: "sales", moduleKey: "sales.dashboard" },
      { label: "客户跟进", to: "/crm/follow-ups", icon: ContactRound, isActive: isCustomerFollowUpRoute, workspace: "sales", moduleKey: "sales.crm" },
      { label: "商机跟踪", to: "/crm/opportunities", icon: CircleDollarSign, isActive: isSalesOpportunityRoute, workspace: "sales", moduleKey: "sales.opportunities" },
      { label: "邮件模板", to: "/crm/email-templates", icon: Mail, isActive: isEmailTemplateRoute, workspace: "sales", moduleKey: "sales.email-templates" },
    ],
  },
  {
    key: "documents",
    label: "单证业务",
    icon: FileText,
    items: [
      { label: "发票管理", to: "/invoices", icon: FileText, isActive: isInvoiceRoute, workspace: "document", moduleKey: "document.invoices" },
      { label: "单据查询", to: "/query/invoices", icon: Search, isActive: isQueryRoute, workspace: "document", moduleKey: "document.query" },
      { label: "付款报销", to: "/payments", icon: CreditCard, isActive: isPaymentRoute, workspace: "document", moduleKey: "document.payments" },
      { label: "任务中心", to: "/jobs", icon: ClipboardList, isActive: isJobRoute, workspace: "document", moduleKey: "document.jobs" },
    ],
  },
  {
    key: "suppliers",
    label: "供应链资料",
    icon: Factory,
    items: [{ label: "供应商管理", to: "/suppliers", icon: Factory, isActive: isSupplierRoute, workspace: "sales", moduleKey: "sales.suppliers" }],
  },
  {
    key: "single-window",
    label: "单一窗口",
    icon: Network,
    items: [
      { label: "操作中心", to: "/single-window/operation-center", icon: Network, isActive: isSingleWindowOperationRoute, workspace: "document", moduleKey: "document.single-window" },
      { label: "协同看板", to: "/single-window/collaboration", icon: ClipboardList, isActive: isSingleWindowCollaborationRoute, workspace: "document", moduleKey: "document.single-window" },
      { label: "参考词典", to: "/single-window/reference-catalog", icon: Database, isActive: isSingleWindowReferenceCatalogRoute, workspace: "document", moduleKey: "document.single-window" },
    ],
  },
  {
    key: "hs-knowledge",
    label: "知识中心库",
    icon: BookOpen,
    items: [{ label: "HS 编码库", to: "/master-data/hs-knowledge/search", icon: BookOpen, isActive: isHsKnowledgeRoute, workspace: "document", moduleKey: "document.master-data" }],
  },
  {
    key: "master-data",
    label: "基础资料",
    icon: Database,
    items: [{ label: "主数据维护", to: "/master-data", icon: Database, isActive: isMasterDataRoute, workspace: "document", moduleKey: "document.master-data" }],
  },
  {
    key: "tools",
    label: "模板与工具",
    icon: PackageCheck,
    items: [
      { label: "报表设计", to: "/reports/templates", icon: ScrollText, isActive: isReportRoute, workspace: "document", moduleKey: "document.reports" },
      { label: "Excel 模板", to: "/tools/excel", icon: FileSpreadsheet, isActive: isExcelToolsRoute, workspace: "document", moduleKey: "document.excel" },
      { label: "智能 OCR", to: "/tools/ocr", icon: ScanText, isActive: isSmartOcrRoute, workspace: "document", moduleKey: "document.ocr" },
      { label: "装箱模拟", to: "/tools/container-packing", icon: PackageCheck, isActive: isContainerPackingRoute, workspace: "document", moduleKey: "document.container-packing" },
      { label: "今日汇率", to: "/tools/exchange-rates", icon: CircleDollarSign, isActive: isExchangeRateRoute, moduleKey: "common.exchange-rates" },
      { label: "邮件发送", to: "/tools/email", icon: Mail, isActive: isEmailRoute, moduleKey: "common.email" },
    ],
  },
  {
    key: "system",
    label: "系统维护",
    icon: Settings,
    items: [
      { label: "软件更新", to: "/system/update", icon: RefreshCw, isActive: isSystemUpdateRoute, requiresAdmin: true, desktopOnly: true },
      { label: "授权注册", to: "/system/license", icon: KeyRound, isActive: isLicenseRoute, requiresAdmin: true },
      { label: "审计日志", to: "/audit-logs", icon: ShieldCheck, isActive: isAuditLogRoute, requiresAdmin: true, requiresFullEdition: true },
      { label: "账号与权限", to: "/system/access-control", icon: UsersRound, isActive: isAccessControlRoute, requiresAdmin: true, requiresFullEdition: true },
      { label: "系统设置", to: "/settings", icon: Settings, isActive: isSettingsRoute, requiresAdmin: true },
      { label: "关于系统", to: "/system/about", icon: Info, isActive: isAboutRoute, moduleKey: "system.about" },
    ],
  },
];

export function filterWorkspaceNavGroups(capabilities: WorkspaceCapabilities) {
  const enabledModules = Array.isArray(capabilities.enabledModules)
    ? new Set(capabilities.enabledModules.map((moduleKey) => moduleKey.toLowerCase()))
    : null;
  return workspaceNavGroups
    .map((group) => ({
      ...group,
      items: group.items.filter((item) => {
        if (item.requiresAdmin && capabilities.canManageSettings !== true) return false;
        if (item.desktopOnly && capabilities.isDesktopRuntime !== true) return false;
        if (item.requiresFullEdition && !isFullProductEdition(capabilities.productEdition)) return false;
        if (item.workspace === "document" && capabilities.canUseDocumentWorkspace !== true) return false;
        if (item.workspace === "sales" && capabilities.canUseSalesWorkspace !== true) return false;
        if (item.moduleKey && enabledModules && !enabledModules.has(item.moduleKey.toLowerCase())) return false;
        return true;
      }),
    }))
    .filter((group) => group.items.length > 0);
}

export function findActiveWorkspaceNavGroupKey(pathname: string, groups: WorkspaceNavGroupConfig[] = workspaceNavGroups) {
  return groups.find((group) => group.items.some((item) => item.isActive(pathname)))?.key ?? "workspace";
}

export function createInitialWorkspaceNavGroupState(pathname: string, groups: WorkspaceNavGroupConfig[] = workspaceNavGroups) {
  const expanded = new Set(defaultExpandedWorkspaceNavGroups);
  expanded.add(findActiveWorkspaceNavGroupKey(pathname, groups));
  return expanded;
}

export function getWorkspaceContext(pathname: string): WorkspaceContext {
  if (pathname.startsWith("/dashboard") || pathname === "/") {
    return createWorkspaceContext("工作台", "仪表盘", "查看业务概览、近期单据与待办进度", LayoutDashboard);
  }
  if (pathname.startsWith("/crm/follow-ups")) {
    return createWorkspaceContext("客户业务", "客户跟进", "记录客户沟通、下次动作和待办提醒", ContactRound);
  }
  if (pathname.startsWith("/crm/dashboard")) {
    return createWorkspaceContext("客户业务", "销售概览", "查看客户、联系人和近期跟进待办", LayoutDashboard);
  }
  if (pathname.startsWith("/crm/email-templates")) {
    return createWorkspaceContext("客户业务", "邮件模板", "维护业务邮件模板、变量和单封邮件预览", Mail);
  }
  if (pathname.startsWith("/crm/opportunities")) {
    return createWorkspaceContext("客户业务", "商机与报价跟踪", "记录销售阶段、预计金额、概率和下一步动作", CircleDollarSign);
  }
  if (pathname.startsWith("/suppliers")) {
    return createWorkspaceContext("供应链资料", "供应商管理", "维护供应商、主要产品和联系人", Factory);
  }
  if (pathname.startsWith("/settings")) {
    return createWorkspaceContext("系统维护", "系统设置", "集中管理运行目录、数据库、模板、邮件与维护工具", Settings);
  }
  if (pathname.startsWith("/audit-logs")) {
    return createWorkspaceContext("系统维护", "审计日志", "查询、导出与维护关键业务操作记录", ShieldCheck);
  }
  if (pathname.startsWith("/system/access-control")) {
    return createWorkspaceContext("系统维护", "账号与权限", "维护登录账号、岗位、启停状态与模块权限模板", UsersRound);
  }
  if (pathname.startsWith("/tools/ocr")) {
    return createWorkspaceContext("模板与工具", "智能 OCR", "识别扫描件和图片文字，并保留本地离线处理", ScanText);
  }
  if (pathname.startsWith("/tools/container-packing")) {
    return createWorkspaceContext("模板与工具", "装柜模拟器", "维护货物与柜型，分析空间利用和装载重心", PackageCheck);
  }
  if (pathname.startsWith("/tools/exchange-rates")) {
    return createWorkspaceContext("模板与工具", "今日汇率", "查看常用币种汇率并维护业务换算口径", CircleDollarSign);
  }
  if (pathname.startsWith("/tools/email")) {
    return createWorkspaceContext("模板与工具", "邮件发送", "配置收件信息并发送单据、报表与附件", Mail);
  }
  if (pathname.startsWith("/system/update")) {
    return createWorkspaceContext("系统维护", "软件更新", "检查版本、查看更新说明并交由桌面更新器安装", RefreshCw);
  }
  if (pathname.startsWith("/system/license")) {
    return createWorkspaceContext("系统维护", "授权注册", "查看设备机器码、试用状态与离线授权信息", KeyRound);
  }
  if (pathname.startsWith("/system/about")) {
    return createWorkspaceContext("系统维护", "关于系统", "查看产品版本、运行环境与存储策略", Info);
  }
  if (pathname.startsWith("/query")) {
    return createWorkspaceContext("单证业务", "单据查询", "按日期、客户和关键字检索并导出业务数据", Search);
  }
  if (pathname.startsWith("/jobs")) {
    return createWorkspaceContext("单证业务", "任务中心", "跟踪导入、导出、报表和文件处理任务", ClipboardList);
  }
  if (pathname.startsWith("/tools/excel")) {
    return createWorkspaceContext("模板与工具", "Excel 模板与托单", "导入业务数据、导出模板并生成订舱托单副本", FileSpreadsheet);
  }
  if (pathname.startsWith("/reports")) {
    return createWorkspaceContext("模板与工具", "报表设计", "维护单据模板、可视化版式和打印预览", ScrollText);
  }
  if (pathname.startsWith("/single-window")) {
    return getSingleWindowWorkspaceContext(pathname);
  }
  if (pathname.startsWith("/master-data/hs-knowledge")) {
    return createWorkspaceContext("知识中心库", "HS 编码知识中心", "查询、维护和迁移本公司的税则与申报经验", BookOpen);
  }
  if (pathname.startsWith("/master-data")) {
    return createWorkspaceContext("基础资料", "主数据维护", "统一维护客户、出口商、商品、港口、单位与 HS 编码", Database);
  }
  if (pathname.includes("/payments/new")) {
    return createWorkspaceContext("单证业务", "新建付款报销", "录入付款、费用和报销信息并生成凭证", CreditCard);
  }
  if (/\/payments\/\d+/.test(pathname)) {
    return createWorkspaceContext("单证业务", "付款报销编辑", "维护当前付款记录、费用明细与报表输出", CreditCard);
  }
  if (pathname.startsWith("/payments")) {
    return createWorkspaceContext("单证业务", "付款报销", "查询、维护和输出付款及费用报销记录", CreditCard);
  }
  if (pathname.includes("/invoices/new")) {
    return createWorkspaceContext("单证业务", "新建发票", "录入贸易信息、商品明细和单证资料", FileText);
  }
  if (/\/invoices\/\d+/.test(pathname)) {
    return createWorkspaceContext("单证业务", "发票编辑", "维护当前发票、商品明细、利润与单据输出", FileText);
  }
  if (pathname.startsWith("/invoices")) {
    return createWorkspaceContext("单证业务", "发票管理", "管理出口发票、数据导入导出与业务流转", FileText);
  }
  return createWorkspaceContext("工作台", "仪表盘", "查看业务概览、近期单据与待办进度", LayoutDashboard);
}

export function getRequiredWorkspace(pathname: string): "document" | "sales" | null {
  if (pathname.startsWith("/crm/") || pathname.startsWith("/suppliers")) return "sales";
  if (
    pathname.startsWith("/invoices") ||
    pathname.startsWith("/query") ||
    pathname.startsWith("/payments") ||
    pathname.startsWith("/master-data") ||
    pathname.startsWith("/single-window") ||
    pathname.startsWith("/reports") ||
    pathname.startsWith("/jobs") ||
    pathname.startsWith("/tools/excel") ||
    pathname.startsWith("/tools/ocr") ||
    pathname.startsWith("/tools/container-packing")
  ) return "document";
  return null;
}

export function getRequiredModule(pathname: string): string | null {
  if (pathname.startsWith("/crm/dashboard")) return "sales.dashboard";
  if (pathname.startsWith("/crm/follow-ups")) return "sales.crm";
  if (pathname.startsWith("/crm/opportunities")) return "sales.opportunities";
  if (pathname.startsWith("/crm/email-templates")) return "sales.email-templates";
  if (pathname.startsWith("/suppliers")) return "sales.suppliers";
  if (pathname.startsWith("/dashboard")) return "document.dashboard";
  if (pathname.startsWith("/invoices")) return "document.invoices";
  if (pathname.startsWith("/query")) return "document.query";
  if (pathname.startsWith("/payments")) return "document.payments";
  if (pathname.startsWith("/master-data")) return "document.master-data";
  if (pathname.startsWith("/single-window")) return "document.single-window";
  if (pathname.startsWith("/reports")) return "document.reports";
  if (pathname.startsWith("/jobs")) return "document.jobs";
  if (pathname.startsWith("/tools/excel")) return "document.excel";
  if (pathname.startsWith("/tools/ocr")) return "document.ocr";
  if (pathname.startsWith("/tools/container-packing")) return "document.container-packing";
  if (pathname.startsWith("/tools/exchange-rates")) return "common.exchange-rates";
  if (pathname.startsWith("/tools/email")) return "common.email";
  if (pathname.startsWith("/system/about")) return "system.about";
  return null;
}

export function getRequiredRouteAccessLevel(pathname: string): "view" | "operate" {
  return pathname === "/payments/new" ||
    pathname === "/invoices/new" ||
    /^\/master-data\/[^/]+\/new$/.test(pathname) ||
    /^\/single-window\/(coo|acd)\/[^/]+$/.test(pathname)
    ? "operate"
    : "view";
}

export function isAdminOnlyRoute(pathname: string) {
  return pathname.startsWith("/settings") ||
    pathname.startsWith("/system/access-control") ||
    pathname.startsWith("/system/update") ||
    pathname.startsWith("/audit-logs");
}

export function isFullEditionOnlyRoute(pathname: string) {
  return pathname.startsWith("/audit-logs") ||
    pathname.startsWith("/system/access-control");
}

export function isDesktopOnlyRoute(pathname: string) {
  return pathname.startsWith("/system/update");
}

function isFullProductEdition(value: unknown) {
  return typeof value === "string" && value.trim().toLowerCase() === "full";
}

function createWorkspaceContext(section: string, title: string, description: string, icon: LucideIcon): WorkspaceContext {
  return { section, title, description, icon };
}

function getSingleWindowWorkspaceContext(pathname: string): WorkspaceContext {
  if (pathname.startsWith("/single-window/reference-catalog")) {
    return createWorkspaceContext("单一窗口", "参考词典", "维护申报代码、字段选项与 Excel 导入数据", Database);
  }
  if (pathname.startsWith("/single-window/collaboration")) {
    return createWorkspaceContext("单一窗口", "协同看板", "跟踪工作站、协同工单和提交处理进度", ClipboardList);
  }
  if (pathname.startsWith("/single-window/coo")) {
    return createWorkspaceContext("单一窗口", "海关原产地证", "编辑 COO 草稿、审查字段并生成提交交接包", FileText);
  }
  if (pathname.startsWith("/single-window/acd")) {
    return createWorkspaceContext("单一窗口", "报关代理委托", "编辑 ACD 草稿、审查权限项并处理回执", FileText);
  }
  return createWorkspaceContext("单一窗口", "操作中心", "管理提交批次、客户端目录、交接包和回执", Network);
}

export function isDashboardRoute(pathname: string) { return pathname === "/" || pathname.startsWith("/dashboard"); }
export function isLicenseRoute(pathname: string) { return pathname.startsWith("/system/license"); }
export function isAuditLogRoute(pathname: string) { return pathname.startsWith("/audit-logs"); }
export function isAccessControlRoute(pathname: string) { return pathname.startsWith("/system/access-control"); }
function isInvoiceRoute(pathname: string) { return pathname.startsWith("/invoices"); }
function isCustomerFollowUpRoute(pathname: string) { return pathname.startsWith("/crm/follow-ups"); }
function isCrmDashboardRoute(pathname: string) { return pathname.startsWith("/crm/dashboard"); }
function isEmailTemplateRoute(pathname: string) { return pathname.startsWith("/crm/email-templates"); }
function isSalesOpportunityRoute(pathname: string) { return pathname.startsWith("/crm/opportunities"); }
function isSupplierRoute(pathname: string) { return pathname.startsWith("/suppliers"); }
function isQueryRoute(pathname: string) { return pathname.startsWith("/query"); }
function isPaymentRoute(pathname: string) { return pathname.startsWith("/payments"); }
function isMasterDataRoute(pathname: string) { return pathname.startsWith("/master-data") && !isHsKnowledgeRoute(pathname); }
function isHsKnowledgeRoute(pathname: string) { return pathname.startsWith("/master-data/hs-knowledge"); }
function isSingleWindowOperationRoute(pathname: string) { return pathname === "/single-window" || pathname.startsWith("/single-window/operation-center") || pathname.startsWith("/single-window/coo") || pathname.startsWith("/single-window/acd"); }
function isSingleWindowCollaborationRoute(pathname: string) { return pathname.startsWith("/single-window/collaboration"); }
function isSingleWindowReferenceCatalogRoute(pathname: string) { return pathname.startsWith("/single-window/reference-catalog"); }
function isReportRoute(pathname: string) { return pathname.startsWith("/reports"); }
function isJobRoute(pathname: string) { return pathname.startsWith("/jobs"); }
function isExcelToolsRoute(pathname: string) { return pathname.startsWith("/tools/excel"); }
function isSmartOcrRoute(pathname: string) { return pathname.startsWith("/tools/ocr"); }
function isContainerPackingRoute(pathname: string) { return pathname.startsWith("/tools/container-packing"); }
function isExchangeRateRoute(pathname: string) { return pathname.startsWith("/tools/exchange-rates"); }
function isEmailRoute(pathname: string) { return pathname.startsWith("/tools/email"); }
function isSystemUpdateRoute(pathname: string) { return pathname.startsWith("/system/update"); }
function isAboutRoute(pathname: string) { return pathname.startsWith("/system/about"); }
function isSettingsRoute(pathname: string) { return pathname.startsWith("/settings"); }
