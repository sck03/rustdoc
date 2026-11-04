import type { ComponentProps } from "react";
import { PermissionAccessProvider } from "./PermissionAccessContext.tsx";
import { WorkspaceShell } from "./WorkspaceShell.tsx";
import { AppWorkspaceRoutes } from "./AppWorkspaceRoutes.tsx";

export function AuthenticatedWorkspace({ shell, routes }: {
  shell: Omit<ComponentProps<typeof WorkspaceShell>, "children">;
  routes: ComponentProps<typeof AppWorkspaceRoutes>;
}) {
  const user = routes.user;
  return <PermissionAccessProvider subject={user} grants={user.capabilities.moduleAccess}
    permissions={user.capabilities.permissions} canManageSettings={user.capabilities.canManageSettings}>
    <WorkspaceShell {...shell}><AppWorkspaceRoutes {...routes} /></WorkspaceShell>
  </PermissionAccessProvider>;
}
