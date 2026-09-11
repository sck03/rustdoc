import { ShieldCheck } from "lucide-react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { PermissionTemplateManagementPanel } from "./PermissionTemplateManagementPanel.tsx";
import { UserManagementPanel } from "./UserManagementPanel.tsx";
import { useRouteQuery } from "../../ui/useRouteQuery.ts";
import { useConfirmUnsavedChanges } from "../../ui/unsavedChangesGuard.tsx";
import { TaskViewTabs, getTaskViewPanelProps } from "../../ui/TaskViewTabs.tsx";

type ManagementTab = "accounts" | "templates";

export function UserAndPermissionManagementPanel({
  client,
  canManageUsers,
}: {
  client: ExportDocManagerApiClient;
  canManageUsers: boolean;
}) {
  const { params, update } = useRouteQuery();
  const activeTab: ManagementTab = params.get("view") === "templates" ? "templates" : "accounts";
  const confirmDiscardChanges = useConfirmUnsavedChanges();
  const setActiveTab = async (view: ManagementTab) => {
    if (view !== activeTab && await confirmDiscardChanges("切换账号与权限视图")) update({ view }, false);
  };

  if (!canManageUsers) return null;

  return (
    <div className="identity-management-shell">
      <div className="identity-management-header">
        <TaskViewTabs idPrefix="access-control" label="账号与权限管理" value={activeTab} onChange={setActiveTab}
          items={[{ id: "accounts", label: "账号管理" }, { id: "templates", label: "权限方案" }]} />
        <span className="identity-management-security-note"><ShieldCheck size={15} aria-hidden="true" />权限变更立即生效，相关账号需重新登录</span>
      </div>

      <div {...getTaskViewPanelProps("access-control", activeTab)}>
        {activeTab === "accounts" ? (
          <UserManagementPanel client={client} canManageUsers={canManageUsers} />
        ) : (
          <PermissionTemplateManagementPanel client={client} canManageUsers={canManageUsers} />
        )}
      </div>
    </div>
  );
}

export default UserAndPermissionManagementPanel;
