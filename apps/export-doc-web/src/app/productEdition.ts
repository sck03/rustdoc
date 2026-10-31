import { filterWorkspaceNavGroups, getWorkspaceRouteItems, type WorkspaceCapabilities } from "./workspaceNavigation.ts";

export type ProductEdition = "Document" | "Sales" | "Full";

export type ProductEditionPresentation = {
  edition: ProductEdition;
  productName: string;
  displayName: string;
  editionName: string;
  loginTagline: string;
  englishName: string;
  loginHighlights: readonly string[];
  defaultRoute: "/dashboard" | "/crm/dashboard" | "/office/people";
};

const presentations: Record<ProductEdition, ProductEditionPresentation> = {
  Document: {
    edition: "Document",
    productName: "外贸单证管理",
    displayName: "外贸单证管理（单证版）",
    editionName: "单证版",
    loginTagline: "单证制作与交付工作台",
    englishName: "ExportDocManager · Documents",
    loginHighlights: ["制单与校验", "模板与导出", "资料与归档"],
    defaultRoute: "/dashboard",
  },
  Sales: {
    edition: "Sales",
    productName: "外贸业务工作台",
    displayName: "外贸业务工作台（业务员版）",
    editionName: "业务员版",
    loginTagline: "个人外贸工作台",
    englishName: "ExportDocManager · Sales",
    loginHighlights: ["客户与跟进", "商机与报价", "供货商资料"],
    defaultRoute: "/crm/dashboard",
  },
  Full: {
    edition: "Full",
    productName: "业务协同平台",
    displayName: "业务协同平台",
    editionName: "协同版",
    loginTagline: "客户业务、单证、人事与审批协同工作空间",
    englishName: "ExportDocManager · Collaboration",
    loginHighlights: ["业务与单证", "人员与组织", "申请与审批"],
    defaultRoute: "/dashboard",
  },
};

export function normalizeProductEdition(value: unknown): ProductEdition {
  if (value === "Document" || value === "Sales") return value;
  return "Full";
}

const desktopFullPresentation: ProductEditionPresentation = {
  ...presentations.Full,
  displayName: "业务协同平台（Full 单机测试版）",
  editionName: "Full 单机测试版",
};

export function getProductEditionPresentation(value: unknown, isDesktopRuntime = false) {
  const edition = normalizeProductEdition(value);
  return edition === "Full" && isDesktopRuntime ? desktopFullPresentation : presentations[edition];
}

export function getDefaultWorkspaceRoute(capabilities: WorkspaceCapabilities) {
  if (!Array.isArray(capabilities.enabledModules)) return "/access-denied";

  const availableRoutes = new Set(
    getWorkspaceRouteItems(filterWorkspaceNavGroups(capabilities)).map((item) => item.to.split("?")[0]),
  );
  const home = getProductEditionPresentation(capabilities.productEdition).defaultRoute;
  if (availableRoutes.has(home)) return home;
  // Full team accounts enter a useful, authorized workspace for their job.
  if (normalizeProductEdition(capabilities.productEdition) === "Full") {
    const preferred = ["/office/approvals", "/office/announcements", "/office/directory", "/crm/dashboard", "/crm/follow-ups", "/invoices", "/payments", "/reports/templates/manage"];
    return preferred.find(route => availableRoutes.has(route)) ?? [...availableRoutes][0] ?? "/access-denied";
  }
  return "/access-denied";
}
