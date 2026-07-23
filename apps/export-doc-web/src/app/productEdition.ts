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

export function getDefaultWorkspaceRoute(capabilities: {
  canUseDocumentWorkspace?: boolean;
  canUseSalesWorkspace?: boolean;
  enabledModules?: string[];
}) {
  const enabled = new Set((capabilities.enabledModules ?? []).map((moduleKey) => moduleKey.toLowerCase()));
  if (enabled.size > 0) {
    if (enabled.has("document.dashboard")) return "/dashboard";
    if (enabled.has("sales.dashboard")) return "/crm/dashboard";
    if (enabled.has("document.payments")) return "/payments";
    if (enabled.has("document.query")) return "/query/invoices";
    if (enabled.has("system.about")) return "/system/about";
    return "/access-denied";
  }
  if (Array.isArray(capabilities.enabledModules)) return "/access-denied";
  return capabilities.canUseSalesWorkspace === true && capabilities.canUseDocumentWorkspace !== true
    ? "/crm/dashboard"
    : "/dashboard";
}
