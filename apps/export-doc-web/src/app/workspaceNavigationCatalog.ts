import {
  BookOpen, CalendarDays, CircleDollarSign, ClipboardList, ContactRound, CreditCard,
  Database, Factory, FileSpreadsheet, FileText, Info, KeyRound, LayoutDashboard, Mail,
  Network, Package, PackageCheck, RefreshCw, ScanText, ScrollText, Search, Settings,
  ShieldCheck, UsersRound, type LucideIcon,
} from "lucide-react";
import { permissionActions, permissionResources } from "./permissionCatalog.ts";

export type WorkspacePermissionRequirement = { resourceKey: string; action: string };
export type WorkspacePermissionGrant = WorkspacePermissionRequirement & { dataScope?: string };
export type WorkspaceNavItem = {
  label: string;
  description: string;
  keywords?: string;
  to: string;
  icon: LucideIcon;
  isActive: (pathname: string) => boolean;
  requiresAdmin?: boolean;
  desktopOnly?: boolean;
  requiresSystemAdministration?: boolean;
  workspace?: "document" | "sales" | "office";
  moduleKey?: string;
  requiredFeature?: string;
  requiredPermissions?: WorkspacePermissionRequirement[];
  permissionMatch?: "all" | "any";
};
export type WorkspaceCapabilities = {
  canManageSettings?: boolean;
  canManageUsers?: boolean;
  usesOfficeRegister?: boolean;
  canUseDocumentWorkspace?: boolean;
  canUseSalesWorkspace?: boolean;
  isDesktopRuntime?: boolean;
  productEdition?: unknown;
  enabledModules?: string[];
  availableFeatures?: string[];
  permissions?: WorkspacePermissionGrant[];
};
export type WorkspaceNavGroupConfig = {
  key: string;
  label: string;
  shortLabel: string;
  icon: LucideIcon;
  items: WorkspaceNavItem[];
};
export type WorkspaceContext = { section: string; title: string; description: string; icon: LucideIcon };

export const workspaceNavGroups: WorkspaceNavGroupConfig[] = [
  {
    key: "workspace", label: "工作台", shortLabel: "工作台", icon: LayoutDashboard,
    items: [
      { label: "我的待办", description: "查看需要办理的业务事项，进入对应业务继续处理", to: "/worklist", icon: ClipboardList,
        isActive: (path) => path === "/worklist", requiredFeature: "worklist" },
      { label: "单证概览", description: "查看业务金额、近期单据与单证进度", keywords: "仪表盘", to: "/dashboard", icon: LayoutDashboard,
        isActive: isDashboardRoute, workspace: "document", moduleKey: "document.dashboard" },
      { label: "销售概览", description: "查看客户、商机和近期跟进情况", to: "/crm/dashboard", icon: LayoutDashboard,
        isActive: (path) => path.startsWith("/crm/dashboard"), workspace: "sales", moduleKey: "sales.dashboard",
        requiredPermissions: [
          { resourceKey: permissionResources.salesDashboard, action: permissionActions.view },
          { resourceKey: permissionResources.crmCustomers, action: permissionActions.view },
          { resourceKey: permissionResources.salesOpportunities, action: permissionActions.view },
        ] },
      { label: "文件任务", description: "查看导入、导出和报表处理进度，下载结果或重试失败任务", keywords: "任务中心 后台任务 PDF 合并 批量报表 ZIP",
        to: "/jobs", icon: ClipboardList, isActive: (path) => path.startsWith("/jobs"), workspace: "document", moduleKey: "document.jobs" },
    ],
  },
  {
    key: "customers", label: "客户与供应链", shortLabel: "客户", icon: ContactRound,
    items: [
      { label: "客户跟进", description: "维护销售客户，记录沟通和下一步跟进", to: "/crm/follow-ups", icon: ContactRound,
        isActive: (path) => path.startsWith("/crm/follow-ups"), workspace: "sales", moduleKey: "sales.crm",
        requiredPermissions: [
          { resourceKey: permissionResources.crmCustomers, action: permissionActions.view },
          { resourceKey: permissionResources.crmFollowUps, action: permissionActions.view },
        ] },
      { label: "商机与报价", description: "跟踪销售阶段、报价金额和下一步动作", keywords: "商机跟踪", to: "/crm/opportunities", icon: CircleDollarSign,
        isActive: (path) => path.startsWith("/crm/opportunities"), workspace: "sales", moduleKey: "sales.opportunities",
        requiredPermissions: [{ resourceKey: permissionResources.salesOpportunities, action: permissionActions.view }] },
      { label: "供应商管理", description: "维护供应商、联系人、产品和评价", to: "/suppliers", icon: Factory,
        isActive: (path) => path.startsWith("/suppliers"), workspace: "sales", moduleKey: "sales.suppliers",
        requiredPermissions: [{ resourceKey: permissionResources.suppliers, action: permissionActions.view }] },
      { label: "邮件模板", description: "维护可重复使用的业务邮件内容", to: "/crm/email-templates", icon: Mail,
        isActive: (path) => path.startsWith("/crm/email-templates"), workspace: "sales", moduleKey: "sales.email-templates",
        requiredPermissions: [{ resourceKey: permissionResources.emailTemplates, action: permissionActions.view }] },
    ],
  },
  {
    key: "documents", label: "单证与申报", shortLabel: "单证", icon: FileText,
    items: [
      { label: "发票管理", description: "新建和维护出口发票，核对并输出单据", to: "/invoices", icon: FileText,
        isActive: (path) => path.startsWith("/invoices") && !isBusinessAttachmentRoute(path), workspace: "document", moduleKey: "document.invoices" },
      { label: "统计查询", description: "按日期、客户和商品检索业务明细，汇总并导出数据", keywords: "单据查询", to: "/query/invoices", icon: Search,
        isActive: (path) => path.startsWith("/query"), workspace: "document", moduleKey: "document.query" },
      { label: "付款报销", description: "维护付款、费用和报销记录并生成凭证", to: "/payments", icon: CreditCard,
        isActive: (path) => path.startsWith("/payments"), workspace: "document", moduleKey: "document.payments" },
      { label: "业务资料", description: "归档原始资料、确认文件与交付文件，查看历史版本", to: "/business-attachments", icon: FileText,
        isActive: isBusinessAttachmentRoute, workspace: "document", moduleKey: "document.invoices", requiredFeature: "business-attachments" },
      { label: "单一窗口", description: "准备申报资料，处理交接批次与回执", to: "/single-window/operation-center", icon: Network,
        isActive: (path) => path === "/single-window" || path.startsWith("/single-window/operation-center") || path.startsWith("/single-window/coo") || path.startsWith("/single-window/acd"),
        workspace: "document", moduleKey: "document.single-window" },
      { label: "申报词典", description: "维护申报代码、字段选项与参考数据", to: "/single-window/reference-catalog", icon: Database,
        isActive: (path) => path.startsWith("/single-window/reference-catalog"), workspace: "document", moduleKey: "document.declaration-dictionary" },
      { label: "HS 编码知识", description: "查询和维护税则编码、归类与申报经验", to: "/master-data/hs-knowledge/search", icon: BookOpen,
        isActive: isHsKnowledgeRoute, workspace: "document", moduleKey: "document.hs-knowledge" },
    ],
  },
  {
    key: "office", label: "公司行政", shortLabel: "行政", icon: CalendarDays,
    items: [
      { label: "人员档案", description: "查找人员，办理入职、转正、调岗和离职", keywords: "人员信息管理 人事 误录修正", to: "/office/people", icon: UsersRound,
        isActive: (path) => path.startsWith("/office/people"), workspace: "office", moduleKey: "office.people",
        requiredPermissions: [{ resourceKey: permissionResources.officePeople, action: "view-details" }] },
      { label: "公司通讯录", description: "查找同事的工作电话、邮箱和办公地点", keywords: "联系 同事 部门", to: "/office/directory", icon: ContactRound,
        isActive: (path) => path.startsWith("/office/directory"), workspace: "office", moduleKey: "office.people",
        requiredPermissions: [{ resourceKey: permissionResources.officePeople, action: permissionActions.view }] },
      { label: "会议室预约", description: "查看日程，办理会议室预约与钥匙交接", to: "/office/meeting-rooms", icon: CalendarDays,
        isActive: (path) => path.startsWith("/office/meeting-rooms"), workspace: "office", moduleKey: "office.rooms",
        requiredPermissions: [{ resourceKey: permissionResources.officeRooms, action: permissionActions.view }] },
      { label: "物品领用", description: "登记办公物品领用，办理发放、归还与库存补充", to: "/office/supplies", icon: Package,
        isActive: (path) => path.startsWith("/office/supplies"), workspace: "office", moduleKey: "office.supplies",
        requiredPermissions: [{ resourceKey: permissionResources.officeSupplies, action: permissionActions.view }] },
    ],
  },
  {
    key: "resources", label: "资料与工具", shortLabel: "工具", icon: Database,
    items: [
      { label: "基础资料", description: "维护制单使用的客户、出口商、商品、港口和单位", keywords: "主数据维护", to: "/master-data", icon: Database,
        isActive: (path) => path.startsWith("/master-data") && !isHsKnowledgeRoute(path), workspace: "document", moduleKey: "document.master-data" },
      { label: "报表模板管理", description: "选择默认模板，维护模板文件、名称和版式", to: "/reports/templates/manage", icon: ScrollText,
        isActive: (path) => path.startsWith("/reports"), workspace: "document", moduleKey: "document.reports",
        requiredPermissions: [{ resourceKey: permissionResources.reportTemplates, action: permissionActions.view }] },
      { label: "Excel 工具", description: "导入 Excel 数据，导出模板与订舱托单", keywords: "Excel 模板", to: "/tools/excel", icon: FileSpreadsheet,
        isActive: (path) => path.startsWith("/tools/excel"), workspace: "document", moduleKey: "document.excel" },
      { label: "文字识别", description: "从扫描件和图片中提取文字", keywords: "智能 OCR", to: "/tools/ocr", icon: ScanText,
        isActive: (path) => path.startsWith("/tools/ocr"), workspace: "document", moduleKey: "document.ocr" },
      { label: "装柜模拟", description: "配置货物与柜型，查看装载方案和空间利用率", keywords: "装箱模拟", to: "/tools/container-packing", icon: PackageCheck,
        isActive: (path) => path.startsWith("/tools/container-packing"), workspace: "document", moduleKey: "document.container-packing" },
      { label: "今日汇率", description: "查看常用币种汇率与业务换算口径", to: "/tools/exchange-rates", icon: CircleDollarSign,
        isActive: (path) => path.startsWith("/tools/exchange-rates"), moduleKey: "common.exchange-rates" },
      { label: "邮件发送", description: "发送业务邮件和附件，查询投递记录", to: "/tools/email", icon: Mail,
        isActive: (path) => path.startsWith("/tools/email"), moduleKey: "common.email", permissionMatch: "any",
        requiredPermissions: [
          { resourceKey: permissionResources.emailDelivery, action: permissionActions.send },
          { resourceKey: permissionResources.emailDelivery, action: permissionActions.viewDelivery },
        ] },
    ],
  },
  {
    key: "system", label: "系统管理", shortLabel: "设置", icon: Settings,
    items: [
      { label: "系统设置", description: "设置运行环境、邮件和业务参数，备份与维护数据", to: "/settings", icon: Settings,
        isActive: (path) => path.startsWith("/settings"), requiresAdmin: true },
      { label: "账号与权限", description: "管理登录账号、权限方案和数据范围", to: "/system/access-control", icon: UsersRound,
        isActive: isAccessControlRoute, requiresAdmin: true, requiresSystemAdministration: true },
      { label: "组织架构", description: "定义公司、部门层级和负责人", keywords: "组织目录 公司部门 综合部", to: "/system/organization", icon: Network,
        isActive: (path) => path.startsWith("/system/organization"), requiresAdmin: true, requiresSystemAdministration: true },
      { label: "审计日志", description: "查阅和导出关键业务操作记录", to: "/audit-logs", icon: ShieldCheck,
        isActive: isAuditLogRoute, requiresAdmin: true, requiresSystemAdministration: true },
      { label: "软件更新", description: "检查版本、查看更新说明并安装更新", to: "/system/update", icon: RefreshCw,
        isActive: (path) => path.startsWith("/system/update"), requiresAdmin: true, desktopOnly: true },
      { label: "授权注册", description: "查看试用状态，办理软件授权", to: "/system/license", icon: KeyRound,
        isActive: isLicenseRoute, requiresAdmin: true },
      { label: "关于系统", description: "查看产品版本、许可与技术支持信息", to: "/system/about", icon: Info,
        isActive: (path) => path.startsWith("/system/about"), moduleKey: "system.about" },
    ],
  },
];

export function isDashboardRoute(pathname: string) { return pathname === "/" || pathname.startsWith("/dashboard"); }
export function isLicenseRoute(pathname: string) { return pathname.startsWith("/system/license"); }
export function isAuditLogRoute(pathname: string) { return pathname.startsWith("/audit-logs"); }
export function isAccessControlRoute(pathname: string) { return pathname.startsWith("/system/access-control"); }
function isBusinessAttachmentRoute(pathname: string) { return pathname.startsWith("/business-attachments") || /^\/invoices\/\d+\/attachments$/.test(pathname); }
function isHsKnowledgeRoute(pathname: string) { return pathname.startsWith("/master-data/hs-knowledge") || pathname.startsWith("/master-data/hs-codes"); }
