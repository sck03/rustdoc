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
    displayName: "外贸业务综合管理系统（行政版）",
    editionName: "行政版",
    loginTagline: "人员、会议室与物品管理工作台",
    englishName: "Foreign Trade Business Management System",
    defaultRoute: "/office/people",
  },
  Document: {
    edition: "Document",
    productName: "外贸业务综合管理系统",
    displayName: "外贸业务综合管理系统（单证员版）",
    editionName: "单证员版",
    loginTagline: "单证业务工作台",
    englishName: "Foreign Trade Business Management System",
    defaultRoute: "/dashboard",
  },
  Sales: {
    edition: "Sales",
    productName: "外贸业务综合管理系统",
    displayName: "外贸业务综合管理系统（业务员版）",
    editionName: "业务员版",
    loginTagline: "客户与销售工作台",
    englishName: "Foreign Trade Business Management System",
    defaultRoute: "/crm/dashboard",
  },
  Full: {
    edition: "Full",
    productName: "外贸业务综合管理系统",
    displayName: "外贸业务综合管理系统（全功能版）",
    editionName: "全功能版",
    loginTagline: "单证、销售与行政一站式工作台",
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
  const preferredRoutes = [
    "/dashboard",
    "/crm/dashboard",
    "/payments",
    "/query/invoices",
    "/invoices",
    "/crm/follow-ups",
    "/crm/opportunities",
    "/suppliers",
    "/crm/email-templates",
    "/reports/templates/manage",
    "/jobs",
    "/master-data",
    "/single-window/operation-center",
    "/tools/exchange-rates",
    "/office/people",
    "/office/directory",
    "/office/meeting-rooms",
    "/office/supplies",
    "/tools/email",
    "/system/about",
    "/settings",
  ];
  return preferredRoutes.find((route) => availableRoutes.has(route)) ?? "/access-denied";
}
