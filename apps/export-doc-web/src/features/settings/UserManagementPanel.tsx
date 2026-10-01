import { Plus, RefreshCw, Save, Trash2 } from "lucide-react";
import { Link } from "react-router-dom";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { ConfirmationDialog } from "../../ui/ConfirmationDialog.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { useUserManagement } from "./useUserManagement.ts";
import { minimumPasswordLength } from "./userAccountModel.ts";
import { getRolePresentation } from "../../app/userRoles.ts";
import { UserPermissionEditor } from "./UserPermissionEditor.tsx";

export function UserManagementPanel({ client, canManageUsers }: { client: ExportDocManagerApiClient; canManageUsers: boolean }) {
  const { draft, setDraft, selectedUserId, users, search, setSearch, roles, permissionTemplates, availableCompanies, availableDepartments,
    selectedTemplate, selectedRole, message, successMessage, deleteTarget, setDeleteTarget, deleteMutation,
    isBusy, refreshUsers, beginNew, saveUser, deleteSelectedUser, selectUser, patchDraft, changeRole, changeCompany } = useUserManagement(client, canManageUsers);
  if (!canManageUsers) return null;
  return (
    <section className="form-section user-management-section" aria-label="用户与权限">
      <div className="section-header">
        <div>
          <h2>账号管理</h2>
          <p className="section-description">创建和维护登录账号，并通过岗位与权限方案控制界面导航和业务操作。</p>
        </div>
        <div className="toolbar-actions">
          <button className="icon-button" type="button" title="刷新用户" aria-label="刷新用户" disabled={isBusy} onClick={() => void refreshUsers()}>
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          <button className="icon-button" type="button" title="新建用户" aria-label="新建用户" disabled={isBusy} onClick={() => void beginNew()}>
            <Plus size={18} aria-hidden="true" />
          </button>
          <button className="command-button" type="button" disabled={isBusy} onClick={saveUser}>
            <Save size={17} aria-hidden="true" />
            <span>保存</span>
          </button>
          <button className="icon-button" type="button" title="删除用户" aria-label="删除用户" disabled={isBusy || draft.id <= 0} onClick={() => void deleteSelectedUser()}>
            <Trash2 size={18} aria-hidden="true" />
          </button>
        </div>
      </div>

      {message ? <InlineNotice tone="error" title="用户操作失败">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      <label><span>查找账号或分组</span><input type="search" value={search} maxLength={100} placeholder="账号、姓名、岗位、分组或部门" onChange={event => setSearch(event.target.value)} /></label>

      <div className="user-management-layout">
        <ResponsiveTableFrame className="user-management-table-frame" label="用户账号列表">
          <table className="user-management-table">
            <thead>
              <tr>
                <th>账号</th>
                <th>姓名</th>
                <th>角色</th>
                <th>权限方案</th>
                <th>状态</th>
              </tr>
            </thead>
            <tbody>
              {users.length === 0 ? (
                <tr>
                  <td className="empty-cell" colSpan={5}>
                    暂无用户
                  </td>
                </tr>
              ) : (
                users.map((user) => (
                  <tr
                    key={user.id}
                    className={user.id === selectedUserId ? "clickable-row selected-row" : "clickable-row"}
                    tabIndex={0}
                    onClick={() => void selectUser(user)}
                    onKeyDown={(event) => {
                      if (event.key === "Enter" || event.key === " ") {
                        event.preventDefault();
                        void selectUser(user);
                      }
                    }}
                  >
                    <td className="strong-cell">{user.username}</td>
                    <td>{user.fullName || "-"}</td>
                    <td>
                      <span className="role-label">{getRolePresentation(user.role).label}</span>
                    </td>
                    <td>{user.permissionGrants != null ? "账号单独配置" : user.permissionTemplateName || "岗位默认分组"}</td>
                    <td><span className={user.isActive ? "account-status active" : "account-status inactive"}>{user.isActive ? "启用" : "停用"}</span></td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </ResponsiveTableFrame>

        <div className="field-grid user-management-form-grid">
          <label>
            <span>账号</span>
            <input value={draft.username} disabled={isBusy} onChange={(event) => patchDraft("username", event.target.value)} />
          </label>
          <label>
            <span>姓名</span>
            <input value={draft.fullName} disabled={isBusy} onChange={(event) => patchDraft("fullName", event.target.value)} />
          </label>
          <label>
            <span>角色</span>
            <select value={draft.role} disabled={isBusy} onChange={(event) => changeRole(event.target.value)}>
              {roles.map((role) => (
                <option key={role} value={role}>
                  {getRolePresentation(role).label}
                </option>
              ))}
            </select>
            <small className="field-help">{selectedRole.description}</small>
          </label>
          <label>
            <span>权限方案</span>
            <select
              value={draft.permissionTemplateId ?? ""}
              disabled={isBusy || draft.role === "Admin" || draft.permissionGrants !== null}
              onChange={(event) => patchDraft("permissionTemplateId", Number(event.target.value) || null)}
            >
              <option value="">按角色默认方案</option>
              {permissionTemplates.map((template) => (
                <option key={template.id} value={template.id} disabled={!template.isActive && template.id !== draft.permissionTemplateId}>
                  {template.name}{template.isSystem ? "（内置）" : ""}{!template.isActive ? "（已停用）" : ""}
                </option>
              ))}
            </select>
            <small className="field-help">
              {draft.role.toLowerCase() === "admin"
                ? "系统管理员固定使用内置管理员权限"
                : draft.permissionGrants !== null
                  ? "账号使用独立权限；分组变更不再影响此账号"
                : selectedTemplate?.name
                  ? `当前使用：${selectedTemplate.name}`
                  : "未指定时自动使用该岗位的内置方案"}
            </small>
          </label>
          <label>
            <span>所属公司</span>
            <select value={draft.companyScope} disabled={isBusy} onChange={(event) => changeCompany(event.target.value)}>
              <option value="">未分配公司</option>
              {availableCompanies.map((company) => (
                <option key={company.code} value={company.code}>
                  {company.name}（{company.code}）{!company.isActive ? "（已停用）" : ""}
                </option>
              ))}
            </select>
            <small className="field-help">公司代码是数据范围授权键，不能自由输入。</small>
          </label>
          <label>
            <span>所属部门</span>
            <select
              value={draft.departmentId}
              disabled={isBusy || !draft.companyScope}
              onChange={(event) => patchDraft("departmentId", event.target.value)}
            >
              <option value="">未分配部门</option>
              {availableDepartments.map((department) => (
                <option key={department.code} value={department.code}>
                  {department.name}（{department.code}）{!department.isActive ? "（已停用）" : ""}
                </option>
              ))}
            </select>
            <small className="field-help">部门必须属于所选公司；公司变更时会清除不匹配的部门。</small>
          </label>
          <label>
            <span>初始/重置密码</span>
            <input
              type="password"
              value={draft.resetPassword}
              disabled={isBusy}
              onChange={(event) => patchDraft("resetPassword", event.target.value)}
            />
            <small className="field-help">
              {draft.id === 0
                ? `新增账号时必填，至少 ${minimumPasswordLength} 个字符`
                : `留空表示不修改原密码；重置时至少 ${minimumPasswordLength} 个字符`}
            </small>
          </label>
          <label className="settings-check">
            <input type="checkbox" checked={draft.isActive} disabled={isBusy} onChange={(event) => patchDraft("isActive", event.target.checked)} />
            <span>启用账号</span>
          </label>
        </div>
      </div>

      {draft.role !== "Admin" ? <UserPermissionEditor client={client} template={selectedTemplate}
        grants={draft.permissionGrants} disabledModules={draft.disabledModules} busy={isBusy}
        onChange={(permissionGrants, disabledModules) => setDraft(current => ({ ...current, permissionGrants, disabledModules }))} /> : null}

      <p className="section-description">公司和部门在 <Link to="/system/organization">组织架构</Link> 中统一维护。</p>

      {deleteTarget ? (
        <ConfirmationDialog
          title="删除账号"
          description={`确定删除账号“${deleteTarget.username}”吗？`}
          details={[
            "删除后该账号将立即无法登录。",
            "如果账号已有发票或付款等业务数据，系统会阻止删除并提示改为停用。",
          ]}
          confirmLabel="删除账号"
          isBusy={deleteMutation.isPending}
          onCancel={() => setDeleteTarget(null)}
          onConfirm={() => {
            if (deleteTarget.id > 0) {
              deleteMutation.mutate({ id: deleteTarget.id, expectedVersion: deleteTarget.versionNumber }, {
                onSettled: () => setDeleteTarget(null),
              });
            }
          }}
        />
      ) : null}
    </section>
  );
}

export default UserManagementPanel;
