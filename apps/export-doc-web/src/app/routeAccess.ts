import type { ApiUserDto } from "../api/index.ts";
import { hasWorkspaceItemAccess } from "./workspaceAccess.ts";
import { findWorkspaceNavItem, getRequiredRouteAccessLevel } from "./workspaceNavigation.ts";

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
  const item = pathname === "/" ? undefined : findWorkspaceNavItem(pathname);
  return !item || hasWorkspaceItemAccess(item, { ...user.capabilities, canManageSettings: canManageSystem, isDesktopRuntime }, getRequiredRouteAccessLevel(pathname));
}
