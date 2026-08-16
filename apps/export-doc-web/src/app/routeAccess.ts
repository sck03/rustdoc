import type { ApiUserDto } from "../api/index.ts";
import { hasRouteModulePermission } from "./PermissionAccessContext.tsx";
import {
  getRequiredModule,
  getRequiredRouteAccessLevel,
  getRequiredWorkspace,
  isAdminOnlyRoute,
  isDesktopOnlyRoute,
  isFullEditionOnlyRoute,
} from "./workspaceNavigation.ts";

export function isRouteAccessAllowed({
  pathname,
  user,
  canManageSystem,
  isDesktopRuntime,
  isFullEdition,
}: {
  pathname: string;
  user: ApiUserDto;
  canManageSystem: boolean;
  isDesktopRuntime: boolean;
  isFullEdition: boolean;
}) {
  const workspaceAndModuleAllowed = isWorkspaceModuleAccessAllowed(pathname, user);
  const adminAllowed = !isAdminOnlyRoute(pathname) || canManageSystem;
  const runtimeAllowed = !isDesktopOnlyRoute(pathname) || isDesktopRuntime;
  const editionAllowed = !isFullEditionOnlyRoute(pathname) || isFullEdition;
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
  return workspaceAllowed && moduleAllowed;
}
