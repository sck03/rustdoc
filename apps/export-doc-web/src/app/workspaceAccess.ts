import { hasPermission, hasRouteModulePermission } from "./PermissionAccessContext.tsx";
import type { WorkspaceCapabilities, WorkspaceNavItem } from "./workspaceNavigationCatalog.ts";

/** Shared by navigation, search, the landing page and direct route access. */
export function hasWorkspaceItemAccess(item: WorkspaceNavItem, capabilities: WorkspaceCapabilities, level: "view" | "operate" = "view") {
  if (item.requiresAdmin && capabilities.canManageSettings !== true) return false;
  if (item.requiresSystemAdministration && capabilities.canManageUsers !== true) return false;
  if (item.desktopOnly && capabilities.isDesktopRuntime !== true) return false;
  if (item.workspace === "office" && capabilities.isDesktopRuntime === true && capabilities.usesOfficeRegister !== true) return false;
  if (item.workspace === "document" && capabilities.canUseDocumentWorkspace !== true) return false;
  if (item.workspace === "sales" && capabilities.canUseSalesWorkspace !== true) return false;
  if (item.requiredFeature && !capabilities.availableFeatures?.includes(item.requiredFeature)) return false;
  if (item.requiredFeature === "worklist" && !capabilities.canUseDocumentWorkspace && !capabilities.canUseSalesWorkspace) return false;
  if (item.moduleKey && !hasRouteModulePermission(capabilities.moduleAccess, capabilities.enabledModules, item.moduleKey, level)) return false;
  return hasWorkspaceItemPermission(item, capabilities.permissions, level);
}

export function hasWorkspaceItemPermission(item: WorkspaceNavItem, permissions: WorkspaceCapabilities["permissions"], level: "view" | "operate" = "view") {
  const requirements = item.requiredPermissions ?? (item.moduleKey ? [...new Set(["view", level])].map(action => ({ resourceKey: item.moduleKey!, action })) : []);
  const matches = (requirement: { resourceKey: string; action: string }) => hasPermission(permissions, requirement.resourceKey, requirement.action);
  return item.permissionMatch === "any" ? requirements.some(matches) : requirements.every(matches);
}
