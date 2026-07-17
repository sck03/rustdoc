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
      <div className="identity-management-intro">
        <div>
          <span className="identity-management-eyebrow">账号与访问控制</span>
          <h2>让每个岗位只看到需要的功能</h2>
          <p>适用于桌面端、局域网网页端和容器部署；导航显隐与服务端接口权限保持一致。</p>
        </div>
        <span className="identity-management-security-note">
          <ShieldCheck size={18} aria-hidden="true" />
          权限在用户重新登录后生效
        </span>
      </div>

      <div className="identity-management-tabs" role="tablist" aria-label="账号与权限管理">
        <button
          type="button"
          role="tab"
          aria-selected={activeTab === "accounts"}
          className={activeTab === "accounts" ? "identity-management-tab active" : "identity-management-tab"}
          onClick={() => setActiveTab("accounts")}
        >
          <Users size={18} aria-hidden="true" />
          <span><strong>账号管理</strong><small>人员、岗位与启停状态</small></span>
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={activeTab === "templates"}
          className={activeTab === "templates" ? "identity-management-tab active" : "identity-management-tab"}
          onClick={() => setActiveTab("templates")}
        >
          <ShieldCheck size={18} aria-hidden="true" />
          <span><strong>权限模板</strong><small>按岗位配置可见业务模块</small></span>
        </button>
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
