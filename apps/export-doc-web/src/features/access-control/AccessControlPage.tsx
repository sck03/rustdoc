import { RecordWorkspace } from "../../ui/layout/Workspaces.tsx";
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
    <RecordWorkspace className="access-control-surface" aria-label="账号与权限管理">
      <UserAndPermissionManagementPanel client={client} canManageUsers={canManageUsers} />
    </RecordWorkspace>
  );
}

export default AccessControlPage;
