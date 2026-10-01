import { createContext, useContext, useMemo, type ReactNode } from "react";
import type { ApiModuleAccessDto, ApiPermissionGrantDto } from "../api/index.ts";
import { isRecordInPermissionScope, type PermissionRecord, type PermissionSubject } from "./permissionScope.ts";

export type PermissionAccessLevel = "none" | "view" | "operate" | "manage";

type PermissionAccessValue = {
  grants: ReadonlyMap<string, PermissionAccessLevel>;
  permissions: ReadonlyMap<string, string>;
  canManageSettings: boolean;
  subject?: PermissionSubject;
};

const PermissionAccessContext = createContext<PermissionAccessValue>({
  grants: new Map(),
  permissions: new Map(),
  canManageSettings: false,
});

export function PermissionAccessProvider({
  grants,
  permissions,
  canManageSettings = false,
  subject,
  children,
}: {
  grants?: ApiModuleAccessDto[];
  permissions?: ApiPermissionGrantDto[];
  canManageSettings?: boolean;
  subject?: PermissionSubject;
  children: ReactNode;
}) {
  const value = useMemo<PermissionAccessValue>(() => ({
    grants: new Map(
      (grants ?? [])
        .filter((grant) => typeof grant?.moduleKey === "string" && grant.moduleKey.trim().length > 0)
        .map((grant) => [normalizeModuleKey(grant.moduleKey), normalizePermissionAccessLevel(grant.accessLevel)]),
    ),
    permissions: new Map(
      (permissions ?? [])
        .filter((grant) => typeof grant?.resourceKey === "string" && typeof grant?.action === "string")
        .map((grant) => [permissionKey(grant.resourceKey, grant.action), normalizeDataScope(grant.dataScope)]),
    ),
    canManageSettings,
    subject,
  }), [canManageSettings, grants, permissions, subject]);

  return <PermissionAccessContext.Provider value={value}>{children}</PermissionAccessContext.Provider>;
}

export function usePermission(resourceKey: string, action: string, record?: PermissionRecord | null) {
  const { permissions, subject, canManageSettings } = useContext(PermissionAccessContext);
  const dataScope = permissions.get(permissionKey(resourceKey, action)) ?? "";
  return { allowed: dataScope.length > 0 && (record === undefined || isRecordInPermissionScope(dataScope, subject, record, canManageSettings)), dataScope };
}

export function hasPermission(
  grants: ReadonlyArray<{ resourceKey: string; action: string; dataScope?: string }> | undefined,
  resourceKey: string,
  action: string,
) {
  return (grants ?? []).some((grant) =>
    permissionKey(grant?.resourceKey, grant?.action) === permissionKey(resourceKey, action) &&
    normalizeDataScope(grant?.dataScope).length > 0);
}

export function usePermissionCapabilities() {
  return useContext(PermissionAccessContext);
}

export function useModulePermission(moduleKey: string, record?: PermissionRecord | null) {
  const { grants, permissions, subject, canManageSettings } = useContext(PermissionAccessContext);
  const accessLevel = grants.get(normalizeModuleKey(moduleKey)) ?? "none";
  // Reports combine several resources in legacy endpoint metadata. Standard
  // modules authorize each action independently, even when manage ranks higher.
  const allows = (action: PermissionAccessLevel) => moduleKey === "document.reports"
    ? permissionAccessRank(accessLevel) >= permissionAccessRank(action)
    : Boolean(permissions.get(permissionKey(moduleKey, action))) && (record === undefined ||
      isRecordInPermissionScope(permissions.get(permissionKey(moduleKey, action)) ?? "", subject, record, canManageSettings));
  return {
    accessLevel,
    canView: allows("view"),
    canOperate: allows("operate"),
    canManage: allows("manage"),
  };
}

export function hasModulePermission(
  grants: ApiModuleAccessDto[] | undefined,
  moduleKey: string,
  requiredAccessLevel: PermissionAccessLevel = "view",
) {
  const grant = (grants ?? []).find((item) =>
    normalizeModuleKey(item?.moduleKey) === normalizeModuleKey(moduleKey));
  return permissionAccessRank(normalizePermissionAccessLevel(grant?.accessLevel)) >=
    permissionAccessRank(requiredAccessLevel);
}

export function hasRouteModulePermission(
  moduleAccess: ApiModuleAccessDto[] | undefined,
  enabledModules: string[] | undefined,
  moduleKey: string,
  requiredAccessLevel: PermissionAccessLevel = "view",
) {
  if (!enabledModules?.some((item) => normalizeModuleKey(item) === normalizeModuleKey(moduleKey))) return false;
  if (Array.isArray(moduleAccess)) {
    return hasModulePermission(moduleAccess, moduleKey, requiredAccessLevel);
  }
  if (Array.isArray(enabledModules)) {
    return permissionAccessRank(requiredAccessLevel) <= permissionAccessRank("view") &&
      enabledModules.some((item) => normalizeModuleKey(item) === normalizeModuleKey(moduleKey));
  }
  // Capabilities are deny-by-default while the session is loading.  A route
  // may become visible only after the server has supplied an explicit grant.
  return false;
}

export function normalizePermissionAccessLevel(value: unknown): PermissionAccessLevel {
  if (typeof value !== "string") return "none";
  const normalized = value.trim().toLowerCase();
  return normalized === "view" || normalized === "operate" || normalized === "manage"
    ? normalized
    : "none";
}

function permissionAccessRank(accessLevel: PermissionAccessLevel) {
  switch (accessLevel) {
    case "manage": return 3;
    case "operate": return 2;
    case "view": return 1;
    default: return 0;
  }
}

function normalizeModuleKey(value: unknown) {
  return typeof value === "string" ? value.trim().toLowerCase() : "";
}

function permissionKey(resourceKey: unknown, action: unknown) {
  return `${normalizeModuleKey(resourceKey)}\u001f${normalizeModuleKey(action)}`;
}

function normalizeDataScope(value: unknown) {
  if (typeof value !== "string") return "";
  const normalized = value.trim().toLowerCase();
  return normalized === "own" || normalized === "department" || normalized === "company" || normalized === "all"
    ? normalized
    : "";
}
