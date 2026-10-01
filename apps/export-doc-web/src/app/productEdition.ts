import { filterWorkspaceNavGroups, getWorkspaceRouteItems, type WorkspaceCapabilities } from "./workspaceNavigation.ts";

export type ProductEdition = "Document" | "Sales" | "Full" | "Administration";

export type ProductEditionPresentation = {
  edition: ProductEdition;
  productName: string;
  displayName: string;
  editionName: string;
  loginTagline: string;
  englishName: string;
  defaultRoute: "/dashboard" | "/crm/dashboard" | "/office/people";
};

const presentations: Record<ProductEdition, ProductEditionPresentation> = {
  Administration: {
    edition: "Administration",
    productName: "外贸业务综合管理系统",
    displayName: "外贸业务综合管理系统（行政人事版）",
    editionName: "行政人事版",
    loginTagline: "人事、行政与申请审批工作台",
    englishName: "Foreign Trade Business Management System",
    defaultRoute: "/office/people",
  },
  Document: {
    edition: "Document",
    productName: "外贸业务综合管理系统",
    displayName: "外贸业务综合管理系统（单证版）",
    editionName: "单证版",
    loginTagline: "单证制作与交付工作台",
    englishName: "Foreign Trade Business Management System",
    defaultRoute: "/dashboard",
  },
  Sales: {
    edition: "Sales",
    productName: "外贸业务综合管理系统",
    displayName: "外贸业务综合管理系统（业务员版）",
    editionName: "业务员版",
    loginTagline: "个人外贸工作台",
    englishName: "Foreign Trade Business Management System",
    defaultRoute: "/crm/dashboard",
  },
  Full: {
    edition: "Full",
    productName: "外贸业务综合管理系统",
    displayName: "外贸业务综合管理系统（全功能版）",
    editionName: "全功能版",
    loginTagline: "单证、销售、行政与人事一站式工作台",
    englishName: "Foreign Trade Business Management System",
    defaultRoute: "/dashboard",
  },
};

export function normalizeProductEdition(value: unknown): ProductEdition {
  if (value === "Document" || value === "Sales" || value === "Administration") return value;
  return "Full";
}

export function getProductEditionPresentation(value: unknown) {
  return presentations[normalizeProductEdition(value)];
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
