import { useEffect, useMemo, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ApiUserAccountDto, ApiUserListResponse, ApiUserSaveRequest, ExportDocManagerApiClient } from "../../api/index.ts";
import { identityQueryRoots, queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { type UserDraft, createEmptyDraft, createDraftFromUser, buildUserDraftSnapshot, upsertUserList, minimumPasswordLength } from "./userAccountModel.ts";
import { getRolePresentation } from "../../app/userRoles.ts";
import { useRouteQuery } from "../../ui/useRouteQuery.ts";
import { readRouteId } from "../../ui/routeQueryState.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";

type UserCommand = { kind: "save"; body: ApiUserSaveRequest } | { kind: "delete" } | { kind: "refresh" } | { kind: "new" } | { kind: "select"; user: ApiUserAccountDto };

export function useUserManagement(client: ExportDocManagerApiClient, canManageUsers: boolean) {
  const queryClient = useQueryClient();
  const { params } = useRouteQuery();
  const requestedUserId = readRouteId(params.get("userId"));
  const acceptedRoute = useRef<number | null | undefined>(undefined);
  const requestConfirmation = useConfirmation();
  const [selectedUserId, setSelectedUserId] = useState<number | null>(null);
  const [search, setSearch] = useState("");
  const [draft, setDraft] = useState<UserDraft>(() => createEmptyDraft("OfficeEmployee"));
  const [persistedDraftSnapshot, setPersistedDraftSnapshot] = useState(() => buildUserDraftSnapshot(createEmptyDraft("OfficeEmployee")));
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const usersQuery = useQuery({
    queryKey: queryKeys.users(),
    queryFn: ({ signal }) => client.listUsers({ signal }),
    enabled: canManageUsers,
  });

  const roles = useMemo(() => usersQuery.data?.roles?.filter(Boolean) ?? ["Admin", "User", "Sales", "Finance"], [usersQuery.data?.roles]);
  const users = usersQuery.data?.users ?? [];
  const permissionTemplates = usersQuery.data?.permissionTemplates ?? [];
  const companies = usersQuery.data?.companies ?? [];
  const departments = usersQuery.data?.departments ?? [];
  const availableCompanies = companies.filter((item) => item.isActive || item.code === draft.companyScope);
  const availableDepartments = departments.filter((item) =>
    item.companyCode === draft.companyScope && (item.isActive || item.code === draft.departmentId));
  const selectedTemplate = permissionTemplates.find(template => template.id === draft.permissionTemplateId);
  const selectedRole = getRolePresentation(draft.role);
  const currentDraftSnapshot = useMemo(() => buildUserDraftSnapshot(draft), [draft]);
  const hasUnsavedUserChanges = Boolean(
    canManageUsers &&
    currentDraftSnapshot !== persistedDraftSnapshot,
  );

  useEffect(() => {
    if (!canManageUsers || !usersQuery.data) return;
    // A committed navigation has passed the shared unsaved-changes guard.
    const routeChanged = acceptedRoute.current !== requestedUserId;
    acceptedRoute.current = requestedUserId;
    const matching = usersQuery.data.users.find(user => user.id === (routeChanged ? requestedUserId : selectedUserId));
    const next = matching ?? (routeChanged && requestedUserId == null ? usersQuery.data.users[0] : undefined);
    if (next && (routeChanged || (!hasUnsavedUserChanges && next.versionNumber > draft.versionNumber))) {
      applyUser(next);
    } else if (routeChanged && requestedUserId != null && !next) {
      setSelectedUserId(null);
      const empty = createEmptyDraft("OfficeEmployee");
      setDraft(empty);
      setPersistedDraftSnapshot(buildUserDraftSnapshot(empty));
      setMessage("所选用户不存在或已不可见。");
    }
  }, [canManageUsers, selectedUserId, usersQuery.data, requestedUserId, hasUnsavedUserChanges, draft.versionNumber]);

  useEffect(() => {
    if (usersQuery.isError) {
      setMessage(readApiError(usersQuery.error));
      setSuccessMessage(null);
    }
  }, [usersQuery.error, usersQuery.isError]);

  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: hasUnsavedUserChanges,
    message: "当前用户账号有未保存的修改。",
  });

  const scopeFor = (id: number | null) => `users:${canManageUsers}:${requestedUserId}:${id}`;
  const command = useScopedMutation<boolean, UserCommand>(scopeFor(selectedUserId), client, {
    mutationFn: async (action, signal, { checkCurrent, continueInScope }) => {
      if (!canManageUsers) return false;
      if (action.kind !== "save") {
        if (action.kind === "select" && action.user.id === selectedUserId) return false;
        if (!await confirmDiscardChanges({ new: "新建用户", select: "切换用户", refresh: "刷新用户列表", delete: "删除当前用户" }[action.kind])) return false;
        checkCurrent();
      }
      const accept = (next: UserDraft, id: number | null = next.id) => {
        continueInScope(scopeFor(id));
        setSelectedUserId(id); setDraft(next); setPersistedDraftSnapshot(buildUserDraftSnapshot(next));
        setMessage(null); setSuccessMessage(null);
      };
      if (action.kind === "new") {
        const next = createEmptyDraft("OfficeEmployee");
        const template = permissionTemplates.find(t => t.isSystem && t.code === "OfficeEmployee");
        next.permissionTemplateId = template?.id ?? null;
        next.permissionSchemeVersion = template?.publishedVersion ?? null;
        accept(next); return false;
      }
      if (action.kind === "select") { accept(createDraftFromUser(action.user)); return false; }
      if (action.kind === "refresh") {
        await queryClient.cancelQueries({ queryKey: queryKeys.users() }); checkCurrent();
        const response = await client.listUsers({ signal }); checkCurrent();
        queryClient.setQueryData(queryKeys.users(), response);
        const user = response.users.find(user => user.id === selectedUserId);
        if (user) accept(createDraftFromUser(user));
        return false;
      }
      if (action.kind === "delete") {
        const target = users.find(user => user.id === draft.id);
        if (!target) throw new Error("请选择服务器列表中的用户。");
        if (!await requestConfirmation({ title: "删除账号", description: `确定删除账号“${target.username}”吗？`,
          details: ["删除后该账号将立即无法登录。", "已有业务数据的账号不能删除，请改为停用。"], confirmLabel: "删除账号", tone: "danger" })) return false;
        checkCurrent();
        const response = await client.deleteUserAccount({ id: target.id, expectedVersion: target.versionNumber }, { signal }); checkCurrent();
        await queryClient.cancelQueries({ queryKey: queryKeys.users() }); checkCurrent();
        queryClient.setQueryData<ApiUserListResponse>(queryKeys.users(), current => current && ({ ...current, users: current.users.filter(user => user.id !== target.id) }));
        accept(createEmptyDraft("OfficeEmployee"), null);
        setSuccessMessage(response.message || "用户已删除。");
      } else {
        const response = draft.id > 0
          ? await client.updateUserAccount({ id: draft.id, body: action.body }, { signal })
          : await client.createUserAccount({ body: action.body }, { signal });
        checkCurrent();
        await queryClient.cancelQueries({ queryKey: queryKeys.users() }); checkCurrent();
        queryClient.setQueryData<ApiUserListResponse>(queryKeys.users(), current => upsertUserList(current, response.user, roles));
        accept(createDraftFromUser(response.user));
        setSuccessMessage(response.message || "用户已保存。");
      }
      return true;
    },
    refreshKeys: changed => changed ? [...identityQueryRoots, ["office"], ["worklist"]] : [],
    onError: error => { setMessage(readApiError(error)); setSuccessMessage(null); },
  });
  const isBusy = usersQuery.isFetching || command.isPending;
  const beginNew = () => command.mutate({ kind: "new" });

  function applyUser(user: ApiUserAccountDto) {
    const nextDraft = createDraftFromUser(user);
    setSelectedUserId(user.id);
    setDraft(nextDraft);
    setPersistedDraftSnapshot(buildUserDraftSnapshot(nextDraft));
    setMessage(null);
    setSuccessMessage(null);
  }

  const selectUser = (user: ApiUserAccountDto) => command.mutate({ kind: "select", user });
  const refreshUsers = () => command.mutate({ kind: "refresh" });

  function patchDraft<K extends keyof UserDraft>(key: K, value: UserDraft[K]) {
    setDraft((current) => ({ ...current, [key]: value }));
    setSuccessMessage(null);
  }

  function changeRole(role: string) {
    const templateId = role === "Admin" ? null : permissionTemplates.find(t => t.isSystem && t.code === role)?.id ?? null;
    setDraft((current) => ({ ...current, role, permissionTemplateId: templateId, permissionSchemeVersion:permissionTemplates.find(t=>t.id===templateId)?.publishedVersion??null, permissionGrants: null, disabledModules: role === "Admin" ? [] : current.disabledModules, oaAssignedOnly: role === "Admin" ? false : current.oaAssignedOnly || selectedTemplate?.oaAssignedOnly === true }));
    setSuccessMessage(null);
  }

  function changeCompany(companyScope: string) {
    setDraft((current) => {
      const departmentStillValid = departments.some((item) =>
        item.code === current.departmentId && item.companyCode === companyScope && item.isActive);
      return {
        ...current,
        companyScope,
        departmentId: departmentStillValid ? current.departmentId : "",
      };
    });
    setSuccessMessage(null);
  }

  function saveUser() {
    setMessage(null);
    setSuccessMessage(null);
    if (draft.role !== "Admin" && draft.permissionGrants === null && draft.permissionTemplateId == null) {
      setMessage("请选择明确的权限方案，或切换为独立权限配置。");
      return;
    }

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

    command.mutate({ kind: "save", body: {
      username: draft.username.trim(),
      fullName: draft.fullName.trim(),
      role: draft.role,
      permissionTemplateId: draft.permissionTemplateId,
      permissionSchemeVersion: draft.permissionSchemeVersion,
      permissionGrants: draft.permissionGrants,
      disabledModules: draft.disabledModules,
      oaAssignedOnly: draft.oaAssignedOnly,
      departmentId: draft.departmentId.trim(),
      companyScope: draft.companyScope.trim(),
      isActive: draft.isActive,
      resetPassword: draft.resetPassword,
      expectedVersion: draft.id > 0 ? draft.versionNumber : 0,
    } });
  }

  const deleteSelectedUser = () => command.mutate({ kind: "delete" });

  const searchText = search.normalize("NFKC").trim().toLowerCase();
  const filteredUsers = users.filter(user => [user.username, user.fullName, getRolePresentation(user.role).label,
    user.permissionTemplateName, user.companyScope, user.departmentId, user.employee?.employeeNumber].join(" ").normalize("NFKC").toLowerCase().includes(searchText));
  const employee = users.find(user => user.id === draft.id)?.employee;
  return { draft, setDraft, selectedUserId, users: filteredUsers, search, setSearch, roles, permissionTemplates, availableCompanies, availableDepartments,
    employee, selectedTemplate, selectedRole, message, successMessage,
    isBusy, refreshUsers, beginNew, saveUser, deleteSelectedUser, selectUser, patchDraft, changeRole, changeCompany };
}
