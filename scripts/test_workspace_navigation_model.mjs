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
const routeQueryPath = path.join(repoRoot, "apps/export-doc-web/src/ui/routeQueryState.ts").replaceAll("\\", "/");
const permissionNavigationPath = path.join(repoRoot, "apps/export-doc-web/src/features/settings/permissionNavigationModel.ts").replaceAll("\\", "/");
const permissionAccessPath = path
  .join(repoRoot, "apps", "export-doc-web", "src", "app", "PermissionAccessContext.tsx")
  .replaceAll("\\", "/");
const workspaceDevicePath = path
  .join(repoRoot, "apps", "export-doc-web", "src", "app", "workspaceDevice.ts")
  .replaceAll("\\", "/");
const schemePath = path.join(repoRoot, "apps/export-doc-web/src/features/settings/permissionSchemeModel.ts").replaceAll("\\", "/");
fs.writeFileSync(entry, `import * as model from ${JSON.stringify(modelPath)}; import * as product from ${JSON.stringify(productEditionPath)}; import * as permission from ${JSON.stringify(permissionAccessPath)}; import * as device from ${JSON.stringify(workspaceDevicePath)}; import * as scheme from ${JSON.stringify(schemePath)}; globalThis.__model = model; globalThis.__product = product; globalThis.__permission = permission; globalThis.__device = device; globalThis.__scheme = scheme;`, "utf8");
fs.appendFileSync(entry, `import * as routeQuery from ${JSON.stringify(routeQueryPath)}; import * as permissionNavigation from ${JSON.stringify(permissionNavigationPath)}; globalThis.__routeQuery = routeQuery; globalThis.__permissionNavigation = permissionNavigation;`);
const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entry], outfile: bundle, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
await import(pathToFileURL(bundle).href);

const model = globalThis.__model;
const product = globalThis.__product;
const permission = globalThis.__permission;
const device = globalThis.__device;
const assert = (condition, message) => { if (!condition) throw new Error(message); };
const permissionGrant = (resourceKey, action, dataScope = "own") => ({ resourceKey, action, dataScope });
const scheme = globalThis.__scheme;
const savedGrants = [permissionGrant("sales.customers", "view", "department"), permissionGrant("common.product-reference", "view", "all")];
const originalGrants = JSON.stringify(savedGrants);
const editableGrants = scheme.getEditableSchemeGrants(savedGrants, new Map([
  ["sales.customers", { isTechnical: false }], ["common.product-reference", { isTechnical: true }],
]));
assert(editableGrants[scheme.grantKey("sales.customers", "view")] === "department", "scheme drafts retain configured business scopes");
assert(!(scheme.grantKey("common.product-reference", "view") in editableGrants), "copy/save payloads must exclude read-only technical grants");
assert(JSON.stringify(savedGrants) === originalGrants, "scheme projections must not mutate the server snapshot");
assert(scheme.grantKey("unknown", "view") in scheme.getEditableSchemeGrants([permissionGrant("unknown", "view")], new Map()), "unknown grants must reach server validation rather than being silently discarded");
const salesPermissions = [
  permissionGrant("sales.dashboard", "view"),
  permissionGrant("sales.customers", "view"),
  permissionGrant("sales.follow-ups", "view"),
  permissionGrant("sales.opportunities", "view"),
  permissionGrant("sales.email-templates", "view", "department"),
  permissionGrant("sales.suppliers", "view", "company"),
];
assert(model.getWorkspaceContext("/invoices/12").title === "发票编辑", "invoice editor context");
assert(model.getWorkspaceContext("/payments/new").title === "新建付款报销", "payment create context");
assert(model.getWorkspaceContext("/single-window/coo/8").section === "单证与申报", "single-window context");
assert(model.getWorkspaceContext("/crm/follow-ups").title === "客户与跟进", "sales workspace context");
assert(model.getWorkspaceContext("/crm/dashboard").title === "工作概览", "sales dashboard context");
assert(model.getWorkspaceContext("/crm/email-templates").title === "邮件中心", "email template context");
assert(model.getWorkspaceContext("/crm/opportunities").title === "商机与报价", "sales opportunity context");
assert(model.getWorkspaceContext("/suppliers").title === "供应商管理", "supplier workspace context");
assert(model.getWorkspaceContext("/system/access-control").title === "账号与权限", "access control context");
assert(model.getRequiredWorkspace("/crm/follow-ups") === "sales", "sales route access model");
assert(model.getRequiredWorkspace("/suppliers") === "sales", "supplier route access model");
assert(model.getRequiredWorkspace("/crm/email-templates") === "sales", "email template route access model");
assert(model.getRequiredWorkspace("/crm/opportunities") === "sales", "sales opportunity route access model");
assert(model.getRequiredWorkspace("/invoices/12") === "document", "document route access model");
assert(model.findActiveWorkspaceNavGroupKey("/tools/ocr") === "resources", "tools navigation group");
assert(model.findActiveWorkspaceNavGroupKey("/master-data/hs-knowledge/search") === "documents", "classification belongs to document work");
assert(model.findActiveWorkspaceNavGroupKey("/business-attachments") === "documents", "business files belong to document work");
assert(model.findActiveWorkspaceNavGroupKey("/jobs") === "workspace", "file progress is distinct from business entry");
assert(model.workspaceNavGroups.length === 6, "navigation uses six task groups");
assert(model.createInitialWorkspaceNavGroupState("/settings").size === 1, "only the current group starts expanded");
assert(model.createInitialWorkspaceNavGroupState("/settings").has("system"), "active group starts expanded");
const navigationItems = model.workspaceNavGroups.flatMap((group) => group.items);
const routeItems = model.getWorkspaceRouteItems();
assert(navigationItems.length === 28, "primary navigation has 28 business entries");
const allModules = [...new Set(routeItems.flatMap((item) => item.moduleKey ? [item.moduleKey] : []))];
const allPermissions = routeItems.flatMap((item) => item.requiredPermissions ?? [])
  .map((requirement) => permissionGrant(requirement.resourceKey, requirement.action));
const fullNavigationGrants = { enabledModules: allModules, permissions: allPermissions };
const userGroups = model.filterWorkspaceNavGroups({ canUseDocumentWorkspace: true, ...fullNavigationGrants });
const salesGroups = model.filterWorkspaceNavGroups({
  productEdition: "Full",
  canUseSalesWorkspace: true,
  enabledModules: ["sales.dashboard", "sales.crm", "sales.opportunities", "sales.email-templates", "sales.suppliers", "system.about"],
  permissions: salesPermissions,
});
const salesEditionAdminGroups = model.filterWorkspaceNavGroups({ productEdition: "Sales", canManageSettings: true, canUseSalesWorkspace: true, isDesktopRuntime: true, ...fullNavigationGrants });
const browserAdminGroups = model.filterWorkspaceNavGroups({ productEdition: "Full", canManageSettings: true, canManageUsers: true, canUseDocumentWorkspace: true, canUseSalesWorkspace: true, isDesktopRuntime: false, ...fullNavigationGrants });
const fullDesktopCapabilities = { productEdition: "Full", canManageSettings: true, canManageUsers: true, canUseDocumentWorkspace: true, canUseSalesWorkspace: true, usesOfficeRegister: true, isDesktopRuntime: true, ...fullNavigationGrants };
const adminGroups = model.filterWorkspaceNavGroups(fullDesktopCapabilities);
assert(adminGroups.find((group) => group.key === "office")?.items.length === 4, "Full desktop includes every administration entry");
assert(product.getDefaultWorkspaceRoute(fullDesktopCapabilities) === "/dashboard", "Full desktop retains its business home after enabling administration");
for (const item of navigationItems) {
  assert(navigationItems.filter((candidate) => candidate.isActive(item.to)).length === 1, `each route has one navigation owner: ${item.to}`);
  for (const route of item.children ?? [item]) assert(model.getRequiredModule(route.to) === (route.moduleKey ?? null), `leaf route keeps its permission module: ${route.to}`);
  assert(model.getWorkspaceContext(item.to).title === item.label, `page and menu use the same name: ${item.to}`);
}
assert(model.findActiveWorkspaceNavGroupKey("/master-data/hs-codes") === "documents", "HS catalogue must not activate basic data navigation");
assert(model.searchWorkspaceNavGroups("任务中心", adminGroups)[0]?.items[0]?.to === "/jobs", "familiar feature names remain searchable");
assert(model.searchWorkspaceNavGroups("ＥＸＣＥＬ 模板", adminGroups).some((group) => group.items.some((item) => item.to === "/tools/excel")), "search normalizes case, width and whitespace");
assert(model.searchWorkspaceNavGroups("系统设置", salesGroups).length === 0, "search cannot expose unavailable system functions");
assert(model.searchWorkspaceNavGroups("", []).length === 0, "empty permissions never become search results");
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
const financePermissions = [
  permissionGrant("document.report-templates", "view", "department"),
  permissionGrant("common.email-delivery", "send"),
  permissionGrant("common.email-delivery", "view-delivery"),
];
const financeGroups = model.filterWorkspaceNavGroups({
  canUseDocumentWorkspace: true,
  enabledModules: financeModules,
  permissions: financePermissions,
});
const documentClerkRoutes = model.filterWorkspaceNavGroups({
  canUseDocumentWorkspace: true,
  enabledModules: ["document.invoices", "document.hs-knowledge", "document.master-data", "system.about"],
}).flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).map((item) => item.to);
const noPermissionGroups = model.filterWorkspaceNavGroups({ canUseDocumentWorkspace: false, canUseSalesWorkspace: false, enabledModules: [] });
const unresolvedPermissionGroups = model.filterWorkspaceNavGroups({
  canManageSettings: true, canUseDocumentWorkspace: true, canUseSalesWorkspace: true, productEdition: "Full",
});
assert(unresolvedPermissionGroups.length === 0, "missing module grants must not expose navigation even with broad workspace flags");
const partialCrmGroups = model.filterWorkspaceNavGroups({
  canUseSalesWorkspace: true,
  enabledModules: ["sales.crm"],
  permissions: [permissionGrant("sales.customers", "view")],
});
const sendOnlyEmailRoutes = model.filterWorkspaceNavGroups({
  enabledModules: ["common.email"],
  permissions: [permissionGrant("common.email-delivery", "send")],
}).flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).map((item) => item.to);
const deliveryOnlyEmailRoutes = model.filterWorkspaceNavGroups({
  enabledModules: ["common.email"],
  permissions: [permissionGrant("common.email-delivery", "view-delivery")],
}).flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).map((item) => item.to);
const noEmailCapabilityRoutes = model.filterWorkspaceNavGroups({
  enabledModules: ["common.email"],
  permissions: [],
}).flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).map((item) => item.to);
const financeRoutes = financeGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).map((item) => item.to);
assert(!userGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/audit-logs"), "audit hidden for normal user");
assert(!userGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/crm/follow-ups"), "sales hidden for document user");
assert(salesGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/crm/follow-ups"), "sales workspace visible for salesperson");
assert(salesGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/crm/dashboard"), "sales dashboard visible for salesperson");
assert(!salesGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/dashboard"), "duplicate generic dashboard hidden for salesperson");
assert(salesGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/suppliers"), "supplier workspace visible for salesperson");
assert(salesGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/crm/email-templates"), "email templates visible for salesperson");
assert(salesGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/crm/opportunities"), "sales opportunities visible for salesperson");
assert(!salesGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/invoices"), "documents hidden for salesperson");
assert(!salesGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/master-data"), "document master data hidden for salesperson");
assert(!salesGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => ["/reports/templates/manage", "/tools/excel", "/tools/ocr", "/tools/container-packing"].includes(item.to)), "document-only tools hidden for salesperson");
assert(!salesGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => ["/system/update", "/system/license", "/audit-logs", "/settings"].includes(item.to)), "administrative navigation hidden for salesperson account");
assert(salesGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/system/about"), "about remains visible for salesperson account");
assert(salesEditionAdminGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/settings"), "sales edition administrator keeps settings");
assert(salesEditionAdminGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/system/update"), "desktop administrator keeps updater");
assert(!salesEditionAdminGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/audit-logs"), "audit hidden outside full edition");
assert(!browserAdminGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/system/update"), "browser administrator does not see desktop updater");
assert(browserAdminGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/settings"), "browser administrator keeps server settings");
assert(browserAdminGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/system/access-control"), "browser administrator sees access control");
assert(adminGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/audit-logs"), "audit visible for admin");
assert(financeRoutes.includes("/payments"), "finance payments visible");
assert(financeRoutes.includes("/query/invoices"), "finance query visible");
assert(financeRoutes.includes("/tools/ocr"), "finance OCR visible");
assert(financeRoutes.includes("/reports/templates/manage"), "finance report template management visible");
assert(financeRoutes.includes("/tools/exchange-rates"), "finance exchange rate visible");
assert(financeRoutes.includes("/tools/email"), "finance email visible");
assert(financeRoutes.includes("/system/about"), "finance about visible");
assert(!financeRoutes.some((route) => ["/dashboard", "/invoices", "/master-data", "/tools/excel", "/tools/container-packing", "/crm/dashboard"].includes(route)), "finance hidden modules stay hidden");
assert(documentClerkRoutes.includes("/master-data/hs-knowledge/search"), "document clerk sees HS knowledge query");
assert(documentClerkRoutes.includes("/master-data"), "document clerk sees scoped master-data maintenance");
assert(noPermissionGroups.length === 0, "explicit empty permission template exposes no navigation");
assert(partialCrmGroups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item])).some((item) => item.to === "/crm/follow-ups"), "customer view alone exposes its directory within the customer workspace");
assert(sendOnlyEmailRoutes.includes("/tools/email"), "email send capability exposes the email page");
assert(deliveryOnlyEmailRoutes.includes("/tools/email"), "delivery-history capability exposes the email page");
assert(!noEmailCapabilityRoutes.includes("/tools/email"), "legacy email module alone cannot expose the email page");
assert(model.hasWorkspacePathPermission("/tools/email", [permissionGrant("common.email-delivery", "send")]), "direct email URL accepts send capability");
assert(model.hasWorkspacePathPermission("/tools/email", [permissionGrant("common.email-delivery", "view-delivery")]), "direct email URL accepts delivery-history capability");
assert(!model.hasWorkspacePathPermission("/tools/email", []), "direct email URL fails closed without a capability");
assert(model.hasWorkspacePathPermission("/crm/follow-ups", [permissionGrant("sales.customers", "view")]), "customer directory needs its own view capability");
assert(!model.hasWorkspacePathPermission("/crm/follow-ups", []), "customer route denies absent capabilities");
assert(model.hasWorkspacePathPermission("/crm/follow-ups", [permissionGrant("sales.customers", "view"), permissionGrant("sales.follow-ups", "view")]), "direct URL accepts complete page capabilities");
assert(model.getRequiredModule("/payments/8") === "document.payments", "payment route module guard");
assert(model.getRequiredModule("/crm/follow-ups") === "sales.crm", "sales route module guard");
assert(model.getRequiredModule("/master-data/hs-knowledge/search") === "document.hs-knowledge", "HS knowledge uses its own route guard");
assert(model.getRequiredModule("/master-data/hs-codes") === "document.hs-knowledge", "HS catalogue uses its own route guard");
assert(model.getRequiredRouteAccessLevel("/invoices/new") === "operate", "new invoice route requires operate");
assert(model.getRequiredRouteAccessLevel("/master-data/products/new") === "operate", "new master-data route requires operate");
assert(model.getRequiredRouteAccessLevel("/single-window/coo/8") === "operate", "COO editor route requires operate");
assert(model.getRequiredRouteAccessLevel("/single-window/acd/8") === "operate", "ACD editor route requires operate");
assert(model.getRequiredRouteAccessLevel("/invoices/8") === "view", "invoice detail route permits view");
assert(product.getDefaultWorkspaceRoute({ canUseDocumentWorkspace: true, enabledModules: financeModules, permissions: financePermissions }) === "/payments", "finance default route");
assert(product.getDefaultWorkspaceRoute({ canUseSalesWorkspace: true, enabledModules: ["sales.dashboard"], permissions: salesPermissions }) === "/crm/dashboard", "sales edition default route");
assert(product.getDefaultWorkspaceRoute({ canUseSalesWorkspace: true, enabledModules: ["sales.opportunities"], permissions: [permissionGrant("sales.opportunities", "view")] }) === "/crm/opportunities", "custom permission template lands on its first usable route");
assert(product.getDefaultWorkspaceRoute({ enabledModules: [] }) === "/access-denied", "empty permission template uses access denied route");
assert(product.getProductEditionPresentation("Document").displayName === "外贸业务综合管理系统（单证员版）", "document edition brand name");
assert(product.getProductEditionPresentation("Sales").displayName === "外贸业务综合管理系统（业务员版）", "sales edition brand name");
assert(product.getProductEditionPresentation("Full").displayName === "外贸业务综合管理系统（全功能版）", "full edition brand name");
assert(new Set(["Document", "Sales", "Full"].map((edition) => product.getProductEditionPresentation(edition).productName)).size === 1, "all editions share the same product brand");
assert(device.getWorkspaceDeviceCapabilities("phone").canUseDenseWorkbench === false, "phone blocks dense workbench");
assert(device.getWorkspaceDeviceCapabilities("phone").canImportExport === false, "phone blocks import and export operations");
assert(device.getWorkspaceDeviceCapabilities("phone").canUseAdvancedTools === false, "phone blocks advanced tools");
assert(device.getWorkspaceDeviceCapabilities("tablet").canImportExport === false, "touch-only tablet blocks file operations");
assert(device.getWorkspaceDeviceCapabilities("tablet").canUseAdvancedTools === false, "touch-only tablet blocks advanced tools");
assert(device.getWorkspaceDeviceCapabilities("tablet", true).canUseDenseWorkbench === true, "tablet with fine pointer enables dense workbench");
assert(device.getWorkspaceDeviceCapabilities("tablet", true).canImportExport === true, "tablet with fine pointer enables file operations");
assert(device.getWorkspaceDeviceCapabilities("desktop").canUseBatchOperations === true, "desktop enables batch operations");
assert(device.resolveWorkspaceDeviceProfile(false, false, true).capabilities.canImportExport === true, "medium fine-pointer profile enables file operations");
assert(device.resolveWorkspaceDeviceProfile(true, false, true).capabilities.canImportExport === false, "phone width blocks file operations even with a fine pointer");
assert(device.resolveWorkspaceDeviceMode(false, false, true) === "tablet", "medium viewport remains tablet even with a fine pointer");
assert(device.resolveWorkspaceDeviceMode(true, false, true) === "phone", "phone width remains phone even with a fine pointer");
assert(device.resolveWorkspaceDeviceMode(false, true, false) === "desktop", "desktop width remains desktop on touch hardware");
assert(device.resolveWorkspaceDeviceMode(false, false, false) === "tablet", "touch-only medium viewport uses tablet mode");
assert(device.resolveWorkspaceDeviceMode(true, false, false) === "phone", "touch-only narrow viewport uses phone mode");
assert(permission.hasModulePermission([{ moduleKey: "document.payments", accessLevel: "view" }], "document.payments", "view"), "view grant permits view");
assert(!permission.hasModulePermission([{ moduleKey: "document.payments", accessLevel: "view" }], "document.payments", "operate"), "view grant blocks operate");
assert(permission.hasModulePermission([{ moduleKey: "document.reports", accessLevel: "manage" }], "document.reports", "manage"), "manage grant permits report design");
assert(!permission.hasRouteModulePermission([], [], "system.about", "view"), "explicit empty grants deny route module");
assert(!permission.hasRouteModulePermission(undefined, undefined, "system.about", "view"), "missing grants fail closed");
assert(!permission.hasRouteModulePermission(undefined, ["document.payments"], "document.payments", "operate"), "legacy enabled module list cannot imply operate access");
assert(model.isAdminOnlyRoute("/settings"), "settings route requires administrator");
assert(model.isAdminOnlyRoute("/system/access-control"), "access control route requires administrator");
assert(model.isAdminOnlyRoute("/system/license"), "license registration route requires administrator");
assert(model.isSystemAdministrationRoute("/audit-logs"), "audit route requires system administration capability");
assert(model.isSystemAdministrationRoute("/system/access-control"), "access control route requires system administration capability");
assert(model.isDesktopOnlyRoute("/system/update"), "updater route requires desktop runtime");
const routes = model.searchWorkspaceNavGroups("WebDAV", adminGroups).flatMap(group => group.items);
assert(routes.some(item => item.to === "/settings?section=webDav" && item.locationLabel.includes("系统管理 / 系统设置")), "search resolves settings subfeatures and displays their location");
assert(model.searchWorkspaceNavGroups("WebDAV", salesGroups).length === 0, "feature search preserves administrative boundaries");
const dictionaryOnly = model.filterWorkspaceNavGroups({ canUseDocumentWorkspace: true, enabledModules: ["document.declaration-dictionary"] });
assert(dictionaryOnly[0].items[0].label === "单一窗口" && dictionaryOnly[0].items[0].to === "/single-window/reference-catalog", "a merged area opens its first authorized destination");
assert(model.getRequiredModule("/single-window/reference-catalog") === "document.declaration-dictionary", "merging navigation never merges dictionary permissions");
const routeQuery = globalThis.__routeQuery;
assert(routeQuery.patchRouteQuery("?page=3&keyword=ACME", { supplierId: 135, view: "profile" }) === "?page=3&keyword=ACME&supplierId=135&view=profile", "opening a record retains the list location");
assert(routeQuery.patchRouteQuery("?page=3&supplierId=135", { supplierId: null }) === "?page=3", "returning removes only the record selection");
for (const value of ["0", "-1", "1.5", "2147483648", "1e2", ""]) assert(routeQuery.readRouteId(value) === null, "invalid route IDs are rejected: " + value);
assert(routeQuery.readRouteId("135") === 135, "valid records outside the first page remain addressable");
const mailResources = ["sales.email-templates", "common.email-delivery"].map((key, index) => ({ key, name: index ? "邮件发送与投递" : "邮件模板", group: "邮件", moduleKey: index ? "common.email" : "sales.email-templates", actions: [] }));
assert(globalThis.__permissionNavigation.filterPermissionResources(mailResources, "客户与供应链", "邮件中心").length === 2, "module search follows the merged page while preserving two resource keys");
process.stdout.write("workspace-navigation-model tests passed\n");
