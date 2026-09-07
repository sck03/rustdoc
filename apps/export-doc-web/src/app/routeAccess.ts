import type { ApiUserDto } from "../api/index.ts";
import { hasRouteModulePermission } from "./PermissionAccessContext.tsx";
import {
  getRequiredModule,
  getRequiredRouteAccessLevel,
  getRequiredWorkspace,
  hasWorkspacePathPermission,
  isAdminOnlyRoute,
  isDesktopOnlyRoute,
  isOfficeRoute,
  isSystemAdministrationRoute,
} from "./workspaceNavigation.ts";

export function isRouteAccessAllowed({
  pathname,
  user,
  canManageSystem,
  isDesktopRuntime,
}: {
  pathname: string;
  user: ApiUserDto;
  canManageSystem: boolean;
  isDesktopRuntime: boolean;
}) {
  const workspaceAndModuleAllowed = isWorkspaceModuleAccessAllowed(pathname, user);
  const adminAllowed = !isAdminOnlyRoute(pathname) || canManageSystem;
  const runtimeAllowed = (!isDesktopOnlyRoute(pathname) || isDesktopRuntime) &&
    (!isOfficeRoute(pathname) || !isDesktopRuntime || user.capabilities.usesOfficeRegister);
  const editionAllowed = !isSystemAdministrationRoute(pathname) || user.capabilities.canManageUsers;
  return workspaceAndModuleAllowed && adminAllowed && runtimeAllowed && editionAllowed;
}

export function isWorkspaceModuleAccessAllowed(pathname: string, user: ApiUserDto) {
  const requiredWorkspace = getRequiredWorkspace(pathname);
  const workspaceAllowed = requiredWorkspace === "sales"
    ? user.capabilities.canUseSalesWorkspace
    : requiredWorkspace === "document"
      ? user.capabilities.canUseDocumentWorkspace
      : true;
  const requiredModule = getRequiredModule(pathname);
  const moduleAllowed = !requiredModule || hasRouteModulePermission(
    user.capabilities.moduleAccess,
    user.capabilities.enabledModules,
    requiredModule,
    getRequiredRouteAccessLevel(pathname),
  );
  const capabilityAllowed = hasWorkspacePathPermission(
    pathname,
    user.capabilities.permissions,
  );
  return workspaceAllowed && moduleAllowed && capabilityAllowed;
}
