import type { ApiPermissionGrantDto, ApiUserAccountDto, ApiUserListResponse } from "../../api/index.ts";

export type UserDraft = {
  id: number;
  versionNumber: number;
  username: string;
  fullName: string;
  role: string;
  permissionTemplateId: number | null;
  permissionGrants: ApiPermissionGrantDto[] | null;
  disabledModules: string[];
  departmentId: string;
  companyScope: string;
  isActive: boolean;
  resetPassword: string;
};

export const minimumPasswordLength = 8;

export function createDraftFromUser(user: ApiUserAccountDto): UserDraft {
  return {
    id: user.id,
    versionNumber: user.versionNumber ?? 1,
    username: user.username ?? "",
    fullName: user.fullName ?? "",
    role: user.role || "User",
    permissionTemplateId: user.permissionTemplateId ?? null,
    permissionGrants: user.permissionGrants ?? null,
    disabledModules: user.disabledModules ?? [],
    departmentId: user.departmentId ?? "",
    companyScope: user.companyScope ?? "",
    isActive: user.isActive,
    resetPassword: "",
  };
}

export function createEmptyDraft(role: string): UserDraft {
  return {
    id: 0,
    versionNumber: 0,
    username: "",
    fullName: "",
    role: role || "User",
    permissionTemplateId: null,
    permissionGrants: null,
    disabledModules: [],
    departmentId: "",
    companyScope: "",
    isActive: true,
    resetPassword: "",
  };
}

export function buildUserDraftSnapshot(draft: UserDraft) {
  return JSON.stringify({
    id: draft.id,
    username: draft.username,
    fullName: draft.fullName,
    role: draft.role,
    permissionTemplateId: draft.permissionTemplateId,
    permissionGrants: draft.permissionGrants,
    disabledModules: draft.disabledModules,
    departmentId: draft.departmentId,
    companyScope: draft.companyScope,
    isActive: draft.isActive,
    resetPassword: draft.resetPassword,
  });
}

export function upsertUserList(
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
    companies: current?.companies ?? [],
    departments: current?.departments ?? [],
    users: nextUsers.sort((left, right) => {
      if (left.isActive !== right.isActive) {
        return left.isActive ? -1 : 1;
      }

      return left.username.localeCompare(right.username);
    }),
  };
}
