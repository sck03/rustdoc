import { LayoutDashboard } from "lucide-react";
import {
  workspaceNavGroups, type WorkspaceCapabilities, type WorkspaceContext,
  type WorkspaceNavGroupConfig, type WorkspaceNavItem, type WorkspacePermissionGrant,
} from "./workspaceNavigationCatalog.ts";
export {
  workspaceNavGroups, isDashboardRoute, isLicenseRoute, isAuditLogRoute, isAccessControlRoute,
  type WorkspaceCapabilities, type WorkspaceContext, type WorkspaceNavGroupConfig,
  type WorkspaceNavItem, type WorkspacePermissionRequirement,
} from "./workspaceNavigationCatalog.ts";

export function filterWorkspaceNavGroups(capabilities: WorkspaceCapabilities) {
  if (!Array.isArray(capabilities.enabledModules)) return [];
  const enabledModules = new Set(capabilities.enabledModules.map(normalizePermissionPart));
  return workspaceNavGroups.map((group) => ({
    ...group,
    items: group.items.filter((item) => {
      if (item.requiredFeature && !capabilities.availableFeatures?.includes(item.requiredFeature)) return false;
      if (item.requiresAdmin && capabilities.canManageSettings !== true) return false;
      if (item.desktopOnly && capabilities.isDesktopRuntime !== true) return false;
      if (item.workspace === "office" && capabilities.isDesktopRuntime === true && capabilities.usesOfficeRegister !== true) return false;
      if (item.requiresSystemAdministration && capabilities.canManageUsers !== true) return false;
      if (item.workspace === "document" && capabilities.canUseDocumentWorkspace !== true) return false;
      if (item.workspace === "sales" && capabilities.canUseSalesWorkspace !== true) return false;
      if (item.moduleKey && !enabledModules.has(normalizePermissionPart(item.moduleKey))) return false;
      return hasWorkspaceNavItemPermission(item, capabilities.permissions);
    }),
  })).filter((group) => group.items.length > 0);
}

// Search only the caller's authorized navigation, including familiar feature names.
export function searchWorkspaceNavGroups(query: string, groups: WorkspaceNavGroupConfig[]) {
  const terms = query.normalize("NFKC").trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (!terms.length) return groups;
  return groups.map((group) => ({
    ...group,
    items: group.items.filter((item) => {
      const text = `${group.label} ${item.label} ${item.description} ${item.keywords ?? ""}`.normalize("NFKC").toLowerCase();
      return terms.every((term) => text.includes(term));
    }),
  })).filter((group) => group.items.length > 0);
}

function findWorkspaceNavItem(pathname: string) {
  return workspaceNavGroups.flatMap((group) => group.items).find((item) => item.isActive(pathname));
}

export function hasWorkspacePathPermission(pathname: string, permissions: WorkspacePermissionGrant[] | undefined) {
  const item = findWorkspaceNavItem(pathname);
  return !item || hasWorkspaceNavItemPermission(item, permissions);
}

export function hasWorkspaceNavItemPermission(item: WorkspaceNavItem, permissions: WorkspacePermissionGrant[] | undefined) {
  if (!item.requiredPermissions?.length) return true;
  if (!Array.isArray(permissions)) return false;
  const matches = (requirement: WorkspacePermissionGrant) => permissions.some((grant) =>
    normalizePermissionPart(grant.resourceKey) === normalizePermissionPart(requirement.resourceKey) &&
    normalizePermissionPart(grant.action) === normalizePermissionPart(requirement.action) &&
    isKnownDataScope(grant.dataScope));
  return item.permissionMatch === "any"
    ? item.requiredPermissions.some(matches)
    : item.requiredPermissions.every(matches);
}

export function findActiveWorkspaceNavGroupKey(pathname: string, groups: WorkspaceNavGroupConfig[] = workspaceNavGroups) {
  return groups.find((group) => group.items.some((item) => item.isActive(pathname)))?.key ?? groups[0]?.key ?? "";
}

export function createInitialWorkspaceNavGroupState(pathname: string, groups: WorkspaceNavGroupConfig[] = workspaceNavGroups) {
  const activeKey = findActiveWorkspaceNavGroupKey(pathname, groups);
  return new Set(activeKey ? [activeKey] : []);
}

export function getWorkspaceContext(pathname: string): WorkspaceContext {
  const group = workspaceNavGroups.find((candidate) => candidate.items.some((item) => item.isActive(pathname)));
  const item = group?.items.find((candidate) => candidate.isActive(pathname));
  if (!group || !item) return { section: "工作台", title: "工作台", description: "选择需要办理的业务", icon: LayoutDashboard };

  const context = { section: group.label, title: item.label, description: item.description, icon: item.icon };
  if (pathname === "/invoices/new") return { ...context, title: "新建发票" };
  if (/^\/invoices\/\d+$/.test(pathname)) return { ...context, title: "发票编辑" };
  if (pathname === "/payments/new") return { ...context, title: "新建付款报销" };
  if (/^\/payments\/\d+$/.test(pathname)) return { ...context, title: "付款报销编辑" };
  if (pathname.startsWith("/single-window/coo")) return { ...context, title: "海关原产地证", description: "编辑原产地证草稿，核对并生成申报资料" };
  if (pathname.startsWith("/single-window/acd")) return { ...context, title: "报关代理委托", description: "编辑代理委托草稿，核对并处理回执" };
  if (pathname.startsWith("/reports") && !pathname.startsWith("/reports/templates/manage"))
    return { ...context, title: "报表设计", description: "编辑模板版式并查看打印效果" };
  return context;
}

export function getRequiredWorkspace(pathname: string): "document" | "sales" | null {
  const workspace = findWorkspaceNavItem(pathname)?.workspace;
  return workspace === "document" || workspace === "sales" ? workspace : null;
}

export function getRequiredModule(pathname: string): string | null {
  return findWorkspaceNavItem(pathname)?.moduleKey ?? null;
}

export function getRequiredRouteAccessLevel(pathname: string): "view" | "operate" {
  return pathname === "/payments/new" || pathname === "/invoices/new" ||
    /^\/master-data\/[^/]+\/new$/.test(pathname) || /^\/single-window\/(coo|acd)\/[^/]+$/.test(pathname)
    ? "operate" : "view";
}

export function isAdminOnlyRoute(pathname: string) { return findWorkspaceNavItem(pathname)?.requiresAdmin === true; }
export function isSystemAdministrationRoute(pathname: string) { return findWorkspaceNavItem(pathname)?.requiresSystemAdministration === true; }
export function isDesktopOnlyRoute(pathname: string) { return findWorkspaceNavItem(pathname)?.desktopOnly === true; }
export function isOfficeRoute(pathname: string) { return findWorkspaceNavItem(pathname)?.workspace === "office"; }
export function getRequiredFeature(pathname: string) { return findWorkspaceNavItem(pathname)?.requiredFeature ?? null; }

function normalizePermissionPart(value: unknown) { return typeof value === "string" ? value.trim().toLowerCase() : ""; }
function isKnownDataScope(value: unknown) {
  return ["own", "department", "company", "all"].includes(normalizePermissionPart(value));
}
