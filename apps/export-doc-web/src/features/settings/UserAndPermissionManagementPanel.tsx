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
          <p>账号权限在不同使用方式下保持一致，未授权的页面和操作会自动隐藏。</p>
        </div>
        <span className="identity-management-security-note">
          <ShieldCheck size={18} aria-hidden="true" />
          权限变更立即生效，相关账号需重新登录
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
          <span><strong>权限模板</strong><small>配置资源、动作和数据范围</small></span>
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
