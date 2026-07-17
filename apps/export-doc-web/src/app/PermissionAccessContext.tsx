import { createContext, useContext, useMemo, type ReactNode } from "react";
import type { ApiModuleAccessDto } from "../api/index.ts";

export type PermissionAccessLevel = "none" | "view" | "operate" | "manage";

type PermissionAccessValue = {
  grants: ReadonlyMap<string, PermissionAccessLevel>;
  canManageSettings: boolean;
};

const PermissionAccessContext = createContext<PermissionAccessValue>({ grants: new Map(), canManageSettings: false });

export function PermissionAccessProvider({
  grants,
  canManageSettings = false,
  children,
}: {
  grants?: ApiModuleAccessDto[];
  canManageSettings?: boolean;
  children: ReactNode;
}) {
  const value = useMemo<PermissionAccessValue>(() => ({
    grants: new Map(
      (grants ?? []).map((grant) => [grant.moduleKey.toLowerCase(), normalizePermissionAccessLevel(grant.accessLevel)]),
    ),
    canManageSettings,
  }), [canManageSettings, grants]);

  return <PermissionAccessContext.Provider value={value}>{children}</PermissionAccessContext.Provider>;
}

export function usePermissionCapabilities() {
  return useContext(PermissionAccessContext);
}

export function useModulePermission(moduleKey: string) {
  const { grants } = useContext(PermissionAccessContext);
  const accessLevel = grants.get(moduleKey.toLowerCase()) ?? "none";
  return {
    accessLevel,
    canView: permissionAccessRank(accessLevel) >= permissionAccessRank("view"),
    canOperate: permissionAccessRank(accessLevel) >= permissionAccessRank("operate"),
    canManage: permissionAccessRank(accessLevel) >= permissionAccessRank("manage"),
  };
}

export function hasModulePermission(
  grants: ApiModuleAccessDto[] | undefined,
  moduleKey: string,
  requiredAccessLevel: PermissionAccessLevel = "view",
) {
  const grant = (grants ?? []).find((item) => item.moduleKey.toLowerCase() === moduleKey.toLowerCase());
  return permissionAccessRank(normalizePermissionAccessLevel(grant?.accessLevel)) >=
    permissionAccessRank(requiredAccessLevel);
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
