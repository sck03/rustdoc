import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspace = path.join(repoRoot, ".codex-runtime", "workspace-navigation-model-tests");
const entry = path.join(workspace, "entry.ts");
const bundle = path.join(workspace, "bundle.mjs");
fs.rmSync(workspace, { recursive: true, force: true });
fs.mkdirSync(workspace, { recursive: true });

const modelPath = path
  .join(repoRoot, "apps", "export-doc-web", "src", "app", "workspaceNavigation.ts")
  .replaceAll("\\", "/");
const productEditionPath = path
  .join(repoRoot, "apps", "export-doc-web", "src", "app", "productEdition.ts")
  .replaceAll("\\", "/");
const permissionAccessPath = path
  .join(repoRoot, "apps", "export-doc-web", "src", "app", "PermissionAccessContext.tsx")
  .replaceAll("\\", "/");
fs.writeFileSync(entry, `import * as model from ${JSON.stringify(modelPath)}; import * as product from ${JSON.stringify(productEditionPath)}; import * as permission from ${JSON.stringify(permissionAccessPath)}; globalThis.__model = model; globalThis.__product = product; globalThis.__permission = permission;`, "utf8");
const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entry], outfile: bundle, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
await import(pathToFileURL(bundle).href);

const model = globalThis.__model;
const product = globalThis.__product;
const permission = globalThis.__permission;
const assert = (condition, message) => { if (!condition) throw new Error(message); };
assert(model.getWorkspaceContext("/invoices/12").title === "发票编辑", "invoice editor context");
assert(model.getWorkspaceContext("/payments/new").title === "新建付款报销", "payment create context");
assert(model.getWorkspaceContext("/single-window/coo/8").section === "单一窗口", "single-window context");
assert(model.getWorkspaceContext("/crm/follow-ups").title === "客户跟进", "sales workspace context");
assert(model.getWorkspaceContext("/crm/dashboard").title === "销售概览", "sales dashboard context");
assert(model.getWorkspaceContext("/crm/email-templates").title === "邮件模板", "email template context");
assert(model.getWorkspaceContext("/crm/opportunities").title === "商机与报价跟踪", "sales opportunity context");
assert(model.getWorkspaceContext("/suppliers").title === "供应商管理", "supplier workspace context");
assert(model.getWorkspaceContext("/system/access-control").title === "账号与权限", "access control context");
assert(model.getRequiredWorkspace("/crm/follow-ups") === "sales", "sales route access model");
assert(model.getRequiredWorkspace("/suppliers") === "sales", "supplier route access model");
assert(model.getRequiredWorkspace("/crm/email-templates") === "sales", "email template route access model");
assert(model.getRequiredWorkspace("/crm/opportunities") === "sales", "sales opportunity route access model");
assert(model.getRequiredWorkspace("/invoices/12") === "document", "document route access model");
assert(model.findActiveWorkspaceNavGroupKey("/tools/ocr") === "tools", "tools navigation group");
assert(model.createInitialWorkspaceNavGroupState("/settings").has("system"), "active group starts expanded");
const userGroups = model.filterWorkspaceNavGroups({ canUseDocumentWorkspace: true });
const salesGroups = model.filterWorkspaceNavGroups({ productEdition: "Full", canUseSalesWorkspace: true });
const salesEditionAdminGroups = model.filterWorkspaceNavGroups({ productEdition: "Sales", canManageSettings: true, canUseSalesWorkspace: true, isDesktopRuntime: true });
const browserAdminGroups = model.filterWorkspaceNavGroups({ productEdition: "Full", canManageSettings: true, canUseDocumentWorkspace: true, canUseSalesWorkspace: true, isDesktopRuntime: false });
const adminGroups = model.filterWorkspaceNavGroups({ productEdition: "Full", canManageSettings: true, canUseDocumentWorkspace: true, canUseSalesWorkspace: true, isDesktopRuntime: true });
const financeModules = [
  "document.payments",
  "document.query",
  "document.ocr",
  "document.reports",
  "document.payment-reports",
  "document.custom-options",
  "document.reference-data",
  "common.exchange-rates",
  "common.email",
  "system.about",
];
const financeGroups = model.filterWorkspaceNavGroups({ canUseDocumentWorkspace: true, enabledModules: financeModules });
const financeRoutes = financeGroups.flatMap((group) => group.items).map((item) => item.to);
assert(!userGroups.flatMap((group) => group.items).some((item) => item.to === "/audit-logs"), "audit hidden for normal user");
assert(!userGroups.flatMap((group) => group.items).some((item) => item.to === "/crm/follow-ups"), "sales hidden for document user");
assert(salesGroups.flatMap((group) => group.items).some((item) => item.to === "/crm/follow-ups"), "sales workspace visible for salesperson");
assert(salesGroups.flatMap((group) => group.items).some((item) => item.to === "/crm/dashboard"), "sales dashboard visible for salesperson");
assert(!salesGroups.flatMap((group) => group.items).some((item) => item.to === "/dashboard"), "duplicate generic dashboard hidden for salesperson");
assert(salesGroups.flatMap((group) => group.items).some((item) => item.to === "/suppliers"), "supplier workspace visible for salesperson");
assert(salesGroups.flatMap((group) => group.items).some((item) => item.to === "/crm/email-templates"), "email templates visible for salesperson");
assert(salesGroups.flatMap((group) => group.items).some((item) => item.to === "/crm/opportunities"), "sales opportunities visible for salesperson");
assert(!salesGroups.flatMap((group) => group.items).some((item) => item.to === "/invoices"), "documents hidden for salesperson");
assert(!salesGroups.flatMap((group) => group.items).some((item) => item.to === "/master-data"), "document master data hidden for salesperson");
assert(!salesGroups.flatMap((group) => group.items).some((item) => ["/reports/templates", "/tools/excel", "/tools/ocr", "/tools/container-packing"].includes(item.to)), "document-only tools hidden for salesperson");
assert(!salesGroups.flatMap((group) => group.items).some((item) => ["/system/update", "/system/license", "/audit-logs", "/settings"].includes(item.to)), "administrative navigation hidden for salesperson account");
assert(salesGroups.flatMap((group) => group.items).some((item) => item.to === "/system/about"), "about remains visible for salesperson account");
assert(salesEditionAdminGroups.flatMap((group) => group.items).some((item) => item.to === "/settings"), "sales edition administrator keeps settings");
assert(salesEditionAdminGroups.flatMap((group) => group.items).some((item) => item.to === "/system/update"), "desktop administrator keeps updater");
assert(!salesEditionAdminGroups.flatMap((group) => group.items).some((item) => item.to === "/audit-logs"), "audit hidden outside full edition");
assert(!browserAdminGroups.flatMap((group) => group.items).some((item) => item.to === "/system/update"), "browser administrator does not see desktop updater");
assert(browserAdminGroups.flatMap((group) => group.items).some((item) => item.to === "/settings"), "browser administrator keeps server settings");
assert(browserAdminGroups.flatMap((group) => group.items).some((item) => item.to === "/system/access-control"), "browser administrator sees access control");
assert(adminGroups.flatMap((group) => group.items).some((item) => item.to === "/audit-logs"), "audit visible for admin");
assert(financeRoutes.includes("/payments"), "finance payments visible");
assert(financeRoutes.includes("/query/invoices"), "finance query visible");
assert(financeRoutes.includes("/tools/ocr"), "finance OCR visible");
assert(financeRoutes.includes("/reports/templates"), "finance report designer visible");
assert(financeRoutes.includes("/tools/exchange-rates"), "finance exchange rate visible");
assert(financeRoutes.includes("/tools/email"), "finance email visible");
assert(financeRoutes.includes("/system/about"), "finance about visible");
assert(!financeRoutes.some((route) => ["/dashboard", "/invoices", "/master-data", "/tools/excel", "/tools/container-packing", "/crm/dashboard"].includes(route)), "finance hidden modules stay hidden");
assert(model.getRequiredModule("/payments/8") === "document.payments", "payment route module guard");
assert(model.getRequiredModule("/crm/follow-ups") === "sales.crm", "sales route module guard");
assert(model.getRequiredRouteAccessLevel("/invoices/new") === "operate", "new invoice route requires operate");
assert(model.getRequiredRouteAccessLevel("/master-data/products/new") === "operate", "new master-data route requires operate");
assert(model.getRequiredRouteAccessLevel("/single-window/coo/8") === "operate", "COO editor route requires operate");
assert(model.getRequiredRouteAccessLevel("/single-window/acd/8") === "operate", "ACD editor route requires operate");
assert(model.getRequiredRouteAccessLevel("/invoices/8") === "view", "invoice detail route permits view");
assert(product.getDefaultWorkspaceRoute({ enabledModules: financeModules }) === "/payments", "finance default route");
assert(permission.hasModulePermission([{ moduleKey: "document.payments", accessLevel: "view" }], "document.payments", "view"), "view grant permits view");
assert(!permission.hasModulePermission([{ moduleKey: "document.payments", accessLevel: "view" }], "document.payments", "operate"), "view grant blocks operate");
assert(permission.hasModulePermission([{ moduleKey: "document.reports", accessLevel: "manage" }], "document.reports", "manage"), "manage grant permits report design");
assert(model.isAdminOnlyRoute("/settings"), "settings route requires administrator");
assert(model.isAdminOnlyRoute("/system/access-control"), "access control route requires administrator");
assert(model.isFullEditionOnlyRoute("/audit-logs"), "audit route requires full edition");
assert(model.isFullEditionOnlyRoute("/system/access-control"), "access control route requires full edition");
assert(model.isDesktopOnlyRoute("/system/update"), "updater route requires desktop runtime");
process.stdout.write("workspace-navigation-model tests passed\n");
