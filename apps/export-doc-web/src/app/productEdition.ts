import { filterWorkspaceNavGroups, type WorkspaceCapabilities } from "./workspaceNavigation.ts";

export type ProductEdition = "Document" | "Sales" | "Full";

export type ProductEditionPresentation = {
  edition: ProductEdition;
  productName: string;
  displayName: string;
  editionName: string;
  loginTagline: string;
  englishName: string;
  defaultRoute: "/dashboard" | "/crm/dashboard";
};

const presentations: Record<ProductEdition, ProductEditionPresentation> = {
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
    loginTagline: "单证与销售协同工作台",
    englishName: "Foreign Trade Business Management System",
    defaultRoute: "/dashboard",
  },
};

export function normalizeProductEdition(value: unknown): ProductEdition {
  if (value === "Document" || value === "Sales") return value;
  return "Full";
}

export function getProductEditionPresentation(value: unknown) {
  return presentations[normalizeProductEdition(value)];
}

export function getDefaultWorkspaceRoute(capabilities: WorkspaceCapabilities) {
  if (!Array.isArray(capabilities.enabledModules)) return "/access-denied";

  const availableRoutes = new Set(
    filterWorkspaceNavGroups(capabilities)
      .flatMap((group) => group.items)
      .map((item) => item.to),
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
    "/tools/email",
    "/system/about",
    "/settings",
  ];
  return preferredRoutes.find((route) => availableRoutes.has(route)) ?? "/access-denied";
}
