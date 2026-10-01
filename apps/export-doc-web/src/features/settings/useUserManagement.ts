import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ApiUserAccountDto, ApiUserListResponse, ApiUserSaveRequest, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { type UserDraft, createEmptyDraft, createDraftFromUser, buildUserDraftSnapshot, upsertUserList, minimumPasswordLength } from "./userAccountModel.ts";
import { getRolePresentation } from "../../app/userRoles.ts";

export function useUserManagement(client: ExportDocManagerApiClient, canManageUsers: boolean) {
  const queryClient = useQueryClient();
  const [selectedUserId, setSelectedUserId] = useState<number | null>(null);
  const [search, setSearch] = useState("");
  const [draft, setDraft] = useState<UserDraft>(() => createEmptyDraft("OfficeEmployee"));
  const [persistedDraftSnapshot, setPersistedDraftSnapshot] = useState(() => buildUserDraftSnapshot(createEmptyDraft("OfficeEmployee")));
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<ApiUserAccountDto | null>(null);

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
  const selectedTemplate = permissionTemplates.find((template) => draft.permissionTemplateId != null
    ? template.id === draft.permissionTemplateId : template.isSystem && template.code === draft.role);
  const selectedRole = getRolePresentation(draft.role);

  useEffect(() => {
    if (!canManageUsers || !usersQuery.data) {
      return;
    }

    if (selectedUserId == null && usersQuery.data.users.length > 0) {
      applyUser(usersQuery.data.users[0]);
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
      const savedDraft = createDraftFromUser(response.user);
      setSelectedUserId(response.user.id);
      setDraft(savedDraft);
      setPersistedDraftSnapshot(buildUserDraftSnapshot(savedDraft));
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
    mutationFn: (target: { id: number; expectedVersion: number }) =>
      client.deleteUserAccount(target),
    onSuccess: async (response) => {
      const emptyDraft = createEmptyDraft("OfficeEmployee");
      setSelectedUserId(null);
      setDraft(emptyDraft);
      setPersistedDraftSnapshot(buildUserDraftSnapshot(emptyDraft));
      setMessage(null);
      setSuccessMessage(response.message || "用户已删除。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.users() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const currentDraftSnapshot = useMemo(() => buildUserDraftSnapshot(draft), [draft]);
  const hasUnsavedUserChanges = Boolean(
    canManageUsers &&
    selectedUserId != null &&
    currentDraftSnapshot !== persistedDraftSnapshot,
  );
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: hasUnsavedUserChanges,
    message: "当前用户账号有未保存的修改。",
  });

  const isBusy = usersQuery.isFetching || saveMutation.isPending || deleteMutation.isPending;

  async function beginNew() {
    if (!await confirmDiscardChanges("新建用户")) {
      return;
    }

    const emptyDraft = createEmptyDraft("OfficeEmployee");
    setSelectedUserId(0);
    setDraft(emptyDraft);
    setPersistedDraftSnapshot(buildUserDraftSnapshot(emptyDraft));
    setMessage(null);
    setSuccessMessage(null);
  }

  function applyUser(user: ApiUserAccountDto) {
    const nextDraft = createDraftFromUser(user);
    setSelectedUserId(user.id);
    setDraft(nextDraft);
    setPersistedDraftSnapshot(buildUserDraftSnapshot(nextDraft));
    setMessage(null);
    setSuccessMessage(null);
  }

  async function selectUser(user: ApiUserAccountDto) {
    if (isBusy || user.id === selectedUserId || !await confirmDiscardChanges(`切换到用户“${user.username}”`)) {
      return;
    }

    applyUser(user);
  }

  async function refreshUsers() {
    if (!await confirmDiscardChanges("刷新用户列表")) {
      return;
    }

    const result = await usersQuery.refetch();
    if (selectedUserId && selectedUserId > 0) {
      const refreshedUser = result.data?.users.find((user) => user.id === selectedUserId);
      if (refreshedUser) {
        applyUser(refreshedUser);
      }
    }
  }

  function patchDraft<K extends keyof UserDraft>(key: K, value: UserDraft[K]) {
    setDraft((current) => ({ ...current, [key]: value }));
    setSuccessMessage(null);
  }

  function changeRole(role: string) {
    setDraft((current) => ({ ...current, role, permissionTemplateId: null, permissionGrants: null, disabledModules: [] }));
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
      permissionTemplateId: draft.permissionTemplateId,
      permissionGrants: draft.permissionGrants,
      disabledModules: draft.disabledModules,
      departmentId: draft.departmentId.trim(),
      companyScope: draft.companyScope.trim(),
      isActive: draft.isActive,
      resetPassword: draft.resetPassword,
      expectedVersion: draft.id > 0 ? draft.versionNumber : 0,
    });
  }

  async function deleteSelectedUser() {
    if (draft.id <= 0) {
      setMessage("请选择要删除的用户。");
      setSuccessMessage(null);
      return;
    }

    if (!await confirmDiscardChanges("删除当前用户")) {
      return;
    }

    const persistedUser = users.find((user) => user.id === draft.id);
    if (!persistedUser) {
      setMessage("当前用户已不在服务器列表中，请刷新后重试。");
      setSuccessMessage(null);
      return;
    }

    setDeleteTarget(persistedUser);
  }

  const searchText = search.normalize("NFKC").trim().toLowerCase();
  const filteredUsers = users.filter(user => [user.username, user.fullName, getRolePresentation(user.role).label,
    user.permissionTemplateName, user.companyScope, user.departmentId].join(" ").normalize("NFKC").toLowerCase().includes(searchText));
  return { draft, setDraft, selectedUserId, users: filteredUsers, search, setSearch, roles, permissionTemplates, availableCompanies, availableDepartments,
    selectedTemplate, selectedRole, message, successMessage, deleteTarget, setDeleteTarget, deleteMutation,
    isBusy, refreshUsers, beginNew, saveUser, deleteSelectedUser, selectUser, patchDraft, changeRole, changeCompany };
}
