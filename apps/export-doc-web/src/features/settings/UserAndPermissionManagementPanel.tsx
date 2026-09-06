import { useState } from "react";
import { ShieldCheck, Users } from "lucide-react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { PermissionTemplateManagementPanel } from "./PermissionTemplateManagementPanel.tsx";
import { UserManagementPanel } from "./UserManagementPanel.tsx";

type ManagementTab = "accounts" | "templates";

export function UserAndPermissionManagementPanel({
  client,
  canManageUsers,
}: {
  client: ExportDocManagerApiClient;
  canManageUsers: boolean;
}) {
  const [activeTab, setActiveTab] = useState<ManagementTab>("accounts");

  if (!canManageUsers) return null;

  return (
    <div className="identity-management-shell">
      <div className="identity-management-header">
        <div className="identity-management-tabs" role="tablist" aria-label="账号与权限管理">
          <button
            type="button"
            role="tab"
            aria-selected={activeTab === "accounts"}
            className={activeTab === "accounts" ? "identity-management-tab active" : "identity-management-tab"}
            onClick={() => setActiveTab("accounts")}
          >
            <Users size={18} aria-hidden="true" />
            <strong>账号管理</strong>
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={activeTab === "templates"}
            className={activeTab === "templates" ? "identity-management-tab active" : "identity-management-tab"}
            onClick={() => setActiveTab("templates")}
          >
            <ShieldCheck size={18} aria-hidden="true" />
            <strong>权限方案</strong>
          </button>
        </div>
        <span className="identity-management-security-note"><ShieldCheck size={15} aria-hidden="true" />权限变更立即生效，相关账号需重新登录</span>
      </div>

      <div role="tabpanel">
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
