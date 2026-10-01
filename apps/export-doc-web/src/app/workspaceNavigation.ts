import { LayoutDashboard } from "lucide-react";
import { hasWorkspaceItemAccess, hasWorkspaceItemPermission } from "./workspaceAccess.ts";
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
  function filter(items: WorkspaceNavItem[]): WorkspaceNavItem[] {
    return items.flatMap((item) => {
      if (!hasWorkspaceItemAccess(item, capabilities)) return [];
      const children = item.children ? filter(item.children) : undefined;
      if (children && !children.length) return [];
      const searchItems = item.searchItems ? filter(item.searchItems) : undefined;
      return [{ ...item, children, searchItems, to: children?.[0]?.to ?? item.to }];
    });
  }
  const groups = workspaceNavGroups.map((group) => ({ ...group, items: filter(group.items) }))
    .filter((group) => group.items.length > 0);
  const officeOnly = capabilities.canManageSettings !== true &&
    !capabilities.canUseDocumentWorkspace && !capabilities.canUseSalesWorkspace &&
    [...enabledModules].some(key => key.startsWith("office.")) &&
    [...enabledModules].every(key => key.startsWith("office.") || key === "system.about");
  if (!officeOnly) return groups;
  const officeTasks = groups.find(group => group.key === "workspace")?.items.filter(item => item.workspace === "office") ?? [];
  return groups.filter(group => group.key === "personnel" || group.key === "office")
    .map(group => group.key === "office" ? { ...group, items: [...officeTasks, ...group.items] } : group)
    .concat(!groups.some(group => group.key === "office") && officeTasks.length
      ? [{ ...workspaceNavGroups.find(group => group.key === "office")!, items: officeTasks }] : []);
}

// Search only the caller's authorized navigation, including familiar feature names.
export function searchWorkspaceNavGroups(query: string, groups: WorkspaceNavGroupConfig[]) {
  const terms = query.normalize("NFKC").trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (!terms.length) return groups;
  return groups.map((group) => ({
    ...group,
    items: group.items.flatMap((item) => searchCandidates(item, group.label)).filter((item) => {
      const text = `${group.label} ${item.locationLabel ?? ""} ${item.label} ${item.description} ${item.keywords ?? ""}`.normalize("NFKC").toLowerCase();
      return terms.every((term) => text.includes(term));
    }).filter((item, index, items) => items.findIndex((candidate) => candidate.to === item.to) === index),
  })).filter((group) => group.items.length > 0);
}

function searchCandidates(item: WorkspaceNavItem, parent: string): WorkspaceNavItem[] {
  const locationLabel = `${parent} / ${item.label}`;
  return [{ ...item, locationLabel },
    ...(item.children ?? []).flatMap((child) => searchCandidates(child, locationLabel)),
    ...(item.searchItems ?? []).map((child) => ({ ...child, locationLabel: `${locationLabel} / ${child.label}` })),
  ];
}

export function getWorkspaceRouteItems(groups: WorkspaceNavGroupConfig[] = workspaceNavGroups) {
  return groups.flatMap((group) => group.items.flatMap((item) => item.children ?? [item]));
}

export function findWorkspaceNavItem(pathname: string) {
  // Containers never grant access: direct routes resolve their own leaf requirement.
  const routes = getWorkspaceRouteItems();
  return routes.flatMap(item => item.searchItems ?? []).find(item => !item.to.includes("?") && item.isActive(pathname))
    ?? routes.find((item) => item.isActive(pathname));
}

export function hasWorkspacePathPermission(pathname: string, permissions: WorkspacePermissionGrant[] | undefined) {
  const item = findWorkspaceNavItem(pathname);
  return !item || hasWorkspaceNavItemPermission(item, permissions);
}

export function hasWorkspaceNavItemPermission(item: WorkspaceNavItem, permissions: WorkspacePermissionGrant[] | undefined) {
  return hasWorkspaceItemPermission(item, permissions);
}

export function findActiveWorkspaceNavGroupKey(pathname: string, groups: WorkspaceNavGroupConfig[] = workspaceNavGroups) {
  return groups.find((group) => group.items.some((item) => item.isActive(pathname)))?.key ?? groups[0]?.key ?? "";
}

export function createInitialWorkspaceNavGroupState(pathname: string, groups: WorkspaceNavGroupConfig[] = workspaceNavGroups) {
  const activeKey = findActiveWorkspaceNavGroupKey(pathname, groups);
  return new Set(activeKey ? [activeKey] : []);
}

export function getWorkspaceContext(pathname: string, groups: WorkspaceNavGroupConfig[] = workspaceNavGroups): WorkspaceContext {
  const group = groups.find((candidate) => candidate.items.some((item) => item.isActive(pathname)));
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

function normalizePermissionPart(value: unknown) { return typeof value === "string" ? value.trim().toLowerCase() : ""; }
