import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, RefreshCw, Save, Trash2 } from "lucide-react";
import { ApiUserAccountDto, ApiUserListResponse, ApiUserSaveRequest, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { ConfirmationDialog } from "../../ui/ConfirmationDialog.tsx";
import { readApiError } from "../../ui/formUtils.ts";

type UserDraft = {
  id: number;
  username: string;
  fullName: string;
  role: string;
  permissionTemplateId: number | null;
  departmentId: string;
  companyScope: string;
  isActive: boolean;
  resetPassword: string;
};

const rolePresentation: Record<string, { label: string; description: string }> = {
  Admin: { label: "系统管理员", description: "管理系统设置、账号以及全部业务数据" },
  User: { label: "单证人员", description: "处理发票、付款、单一窗口及日常单证业务" },
  Sales: { label: "业务人员", description: "管理客户、跟进、商机、邮件模板和供应商" },
  Finance: { label: "财务人员", description: "处理付款报销、单据查询、报表、汇率、邮件和 OCR" },
};

const minimumPasswordLength = 8;

export function UserManagementPanel({
  client,
  canManageUsers,
}: {
  client: ExportDocManagerApiClient;
  canManageUsers: boolean;
}) {
  const queryClient = useQueryClient();
  const [selectedUserId, setSelectedUserId] = useState<number | null>(null);
  const [draft, setDraft] = useState<UserDraft>(() => createEmptyDraft("User"));
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [isDeleteConfirmationOpen, setDeleteConfirmationOpen] = useState(false);

  const usersQuery = useQuery({
    queryKey: queryKeys.users(),
    queryFn: () => client.listUsers(),
    enabled: canManageUsers,
  });

  const roles = useMemo(() => usersQuery.data?.roles?.filter(Boolean) ?? ["Admin", "User", "Sales", "Finance"], [usersQuery.data?.roles]);
  const users = usersQuery.data?.users ?? [];
  const permissionTemplates = usersQuery.data?.permissionTemplates?.filter((template) => template.isActive) ?? [];
  const selectedTemplate = permissionTemplates.find((template) => template.id === draft.permissionTemplateId);
  const selectedRole = getRolePresentation(draft.role);

  useEffect(() => {
    if (!canManageUsers || !usersQuery.data) {
      return;
    }

    if (selectedUserId == null && usersQuery.data.users.length > 0) {
      selectUser(usersQuery.data.users[0]);
    }
  }, [canManageUsers, selectedUserId, usersQuery.data]);

  useEffect(() => {
    if (usersQuery.isError) {
      setMessage(readApiError(usersQuery.error));
      setSuccessMessage(null);
    }
  }, [usersQuery.error, usersQuery.isError]);

  const saveMutation = useMutation({
    mutationFn: (body: ApiUserSaveRequest) =>
      draft.id > 0
        ? client.updateUserAccount({ id: draft.id, body })
        : client.createUserAccount({ body }),
    onSuccess: async (response) => {
      setSelectedUserId(response.user.id);
      setDraft(createDraftFromUser(response.user));
      setMessage(null);
      setSuccessMessage(response.message || "用户已保存。");
      queryClient.setQueryData<ApiUserListResponse | undefined>(queryKeys.users(), (current) =>
        upsertUserList(current, response.user, roles),
      );
      await queryClient.invalidateQueries({ queryKey: queryKeys.users() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: number) => client.deleteUserAccount({ id }),
    onSuccess: async (response) => {
      setSelectedUserId(null);
      setDraft(createEmptyDraft("User"));
      setMessage(null);
      setSuccessMessage(response.message || "用户已删除。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.users() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  if (!canManageUsers) {
    return null;
  }

  const isBusy = usersQuery.isFetching || saveMutation.isPending || deleteMutation.isPending;

  function beginNew() {
    setSelectedUserId(0);
    setDraft(createEmptyDraft("User"));
    setMessage(null);
    setSuccessMessage(null);
  }

  function selectUser(user: ApiUserAccountDto) {
    setSelectedUserId(user.id);
    setDraft(createDraftFromUser(user));
    setMessage(null);
    setSuccessMessage(null);
  }

  function patchDraft<K extends keyof UserDraft>(key: K, value: UserDraft[K]) {
    setDraft((current) => ({ ...current, [key]: value }));
    setSuccessMessage(null);
  }

  function changeRole(role: string) {
    const matchingTemplate = permissionTemplates.find((template) => template.code.toLowerCase() === role.toLowerCase());
    setDraft((current) => ({
      ...current,
      role,
      permissionTemplateId: matchingTemplate?.id ?? current.permissionTemplateId,
    }));
    setSuccessMessage(null);
  }

  function saveUser() {
    setMessage(null);
    setSuccessMessage(null);

    if (!draft.username.trim()) {
      setMessage("用户名不能为空。");
      return;
    }

    if (draft.id === 0 && !draft.resetPassword.trim()) {
      setMessage("新增用户需要填写初始密码。");
      return;
    }

    if (draft.resetPassword && draft.resetPassword.length < minimumPasswordLength) {
      setMessage(`密码至少需要 ${minimumPasswordLength} 个字符。`);
      return;
    }

    saveMutation.mutate({
      username: draft.username.trim(),
      fullName: draft.fullName.trim(),
      role: draft.role,
      permissionTemplateId: draft.permissionTemplateId ?? undefined,
      departmentId: draft.departmentId.trim(),
      companyScope: draft.companyScope.trim(),
      isActive: draft.isActive,
      resetPassword: draft.resetPassword,
    });
  }

  function deleteSelectedUser() {
    if (draft.id <= 0) {
      setMessage("请选择要删除的用户。");
      setSuccessMessage(null);
      return;
    }

    setDeleteConfirmationOpen(true);
  }

  return (
    <section className="form-section user-management-section" aria-label="用户与权限">
      <div className="section-header">
        <div>
          <h2>账号管理</h2>
          <p className="section-description">创建和维护登录账号，并通过岗位与权限模板控制界面导航和业务操作。</p>
        </div>
        <div className="toolbar-actions">
          <button className="icon-button" type="button" title="刷新用户" disabled={isBusy} onClick={() => void usersQuery.refetch()}>
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          <button className="icon-button" type="button" title="新建用户" disabled={isBusy} onClick={beginNew}>
            <Plus size={18} aria-hidden="true" />
          </button>
          <button className="command-button" type="button" disabled={isBusy} onClick={saveUser}>
            <Save size={17} aria-hidden="true" />
            <span>保存</span>
          </button>
          <button className="icon-button" type="button" title="删除用户" disabled={isBusy || draft.id <= 0} onClick={deleteSelectedUser}>
            <Trash2 size={18} aria-hidden="true" />
          </button>
        </div>
      </div>

      {message ? <div className="alert">{message}</div> : null}
      {successMessage ? <div className="success-alert">{successMessage}</div> : null}

      <div className="user-management-layout">
        <div className="table-frame user-management-table-frame">
          <table className="user-management-table">
            <thead>
              <tr>
                <th>账号</th>
                <th>姓名</th>
                <th>角色</th>
                <th>权限模板</th>
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
                    onClick={() => selectUser(user)}
                    onKeyDown={(event) => {
                      if (event.key === "Enter" || event.key === " ") {
                        event.preventDefault();
                        selectUser(user);
                      }
                    }}
                  >
                    <td className="strong-cell">{user.username}</td>
                    <td>{user.fullName || "-"}</td>
                    <td>
                      <span className="role-label">{getRolePresentation(user.role).label}</span>
                    </td>
                    <td>{user.permissionTemplateName || "-"}</td>
                    <td><span className={user.isActive ? "account-status active" : "account-status inactive"}>{user.isActive ? "启用" : "停用"}</span></td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

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
            <span>权限模板</span>
            <select
              value={draft.permissionTemplateId ?? ""}
              disabled={isBusy || draft.role.toLowerCase() === "admin"}
              onChange={(event) => patchDraft("permissionTemplateId", Number(event.target.value) || null)}
            >
              <option value="">按角色默认模板</option>
              {permissionTemplates.map((template) => (
                <option key={template.id} value={template.id}>
                  {template.name}{template.isSystem ? "（内置）" : ""}
                </option>
              ))}
            </select>
            <small className="field-help">
              {draft.role.toLowerCase() === "admin"
                ? "系统管理员固定使用内置管理员权限"
                : selectedTemplate?.name
                  ? `当前使用：${selectedTemplate.name}`
                  : "未指定时自动使用该岗位的内置模板"}
            </small>
          </label>
          <label>
            <span>部门</span>
            <input value={draft.departmentId} disabled={isBusy} onChange={(event) => patchDraft("departmentId", event.target.value)} />
          </label>
          <label>
            <span>公司范围</span>
            <input value={draft.companyScope} disabled={isBusy} onChange={(event) => patchDraft("companyScope", event.target.value)} />
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

      {isDeleteConfirmationOpen ? (
        <ConfirmationDialog
          title="删除账号"
          description={`确定删除账号“${draft.username}”吗？`}
          details={[
            "删除后该账号将立即无法登录。",
            "如果账号已有发票或付款等业务数据，系统会阻止删除并提示改为停用。",
          ]}
          confirmLabel="删除账号"
          isBusy={deleteMutation.isPending}
          onCancel={() => setDeleteConfirmationOpen(false)}
          onConfirm={() => {
            if (draft.id > 0) {
              deleteMutation.mutate(draft.id, {
                onSettled: () => setDeleteConfirmationOpen(false),
              });
            }
          }}
        />
      ) : null}
    </section>
  );
}

export default UserManagementPanel;

function createDraftFromUser(user: ApiUserAccountDto): UserDraft {
  return {
    id: user.id,
    username: user.username ?? "",
    fullName: user.fullName ?? "",
    role: user.role || "User",
    permissionTemplateId: user.permissionTemplateId ?? null,
    departmentId: user.departmentId ?? "",
    companyScope: user.companyScope ?? "",
    isActive: user.isActive,
    resetPassword: "",
  };
}

function createEmptyDraft(role: string): UserDraft {
  return {
    id: 0,
    username: "",
    fullName: "",
    role: role || "User",
    permissionTemplateId: null,
    departmentId: "",
    companyScope: "",
    isActive: true,
    resetPassword: "",
  };
}

function getRolePresentation(role?: string) {
  const normalized = role?.trim() || "User";
  return rolePresentation[normalized] ?? {
    label: normalized,
    description: "使用管理员为该岗位配置的权限模板",
  };
}

function upsertUserList(
  current: ApiUserListResponse | undefined,
  user: ApiUserAccountDto,
  fallbackRoles: string[],
): ApiUserListResponse {
  const roles = current?.roles ?? fallbackRoles;
  const users = current?.users ?? [];
  const index = users.findIndex((item) => item.id === user.id);
  const nextUsers = index >= 0
    ? users.map((item) => (item.id === user.id ? user : item))
    : [...users, user];

  return {
    roles,
    permissionTemplates: current?.permissionTemplates ?? [],
    users: nextUsers.sort((left, right) => {
      if (left.isActive !== right.isActive) {
        return left.isActive ? -1 : 1;
      }

      return left.username.localeCompare(right.username);
    }),
  };
}
