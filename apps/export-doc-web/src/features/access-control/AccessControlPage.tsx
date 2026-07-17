import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { UserAndPermissionManagementPanel } from "../settings/UserAndPermissionManagementPanel.tsx";

export function AccessControlPage({
  client,
  canManageUsers,
}: {
  client: ExportDocManagerApiClient;
  canManageUsers: boolean;
}) {
  if (!canManageUsers) return null;

  return (
    <section className="work-surface access-control-surface" aria-label="账号与权限管理">
      <UserAndPermissionManagementPanel client={client} canManageUsers={canManageUsers} />
    </section>
  );
}

export default AccessControlPage;
