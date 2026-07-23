import { useMediaQuery } from "../ui/useMediaQuery.ts";

export type WorkspaceDeviceMode = "phone" | "tablet" | "desktop";

export type WorkspaceDeviceCapabilities = {
  canUseDenseWorkbench: boolean;
  canUseBatchOperations: boolean;
  canImportExport: boolean;
  canUseAdvancedTools: boolean;
};

export const workspacePhoneMaxWidth = 680;
export const workspaceDesktopMinWidth = 1181;

const capabilities: Record<WorkspaceDeviceMode, WorkspaceDeviceCapabilities> = {
  phone: {
    canUseDenseWorkbench: false,
    canUseBatchOperations: false,
    canImportExport: false,
    canUseAdvancedTools: false,
  },
  tablet: {
    canUseDenseWorkbench: false,
    canUseBatchOperations: false,
    canImportExport: false,
    canUseAdvancedTools: false,
  },
  desktop: {
    canUseDenseWorkbench: true,
    canUseBatchOperations: true,
    canImportExport: true,
    canUseAdvancedTools: true,
  },
};

export function useWorkspaceDeviceMode(): WorkspaceDeviceMode {
  const isPhone = useMediaQuery(`(max-width: ${workspacePhoneMaxWidth}px)`);
  const isDesktop = useMediaQuery(`(min-width: ${workspaceDesktopMinWidth}px)`);
  if (isPhone) return "phone";
  return isDesktop ? "desktop" : "tablet";
}

export function getWorkspaceDeviceCapabilities(mode: WorkspaceDeviceMode) {
  return capabilities[mode];
}

export function getWorkspaceDeviceLabel(mode: WorkspaceDeviceMode) {
  if (mode === "phone") return "手机端";
  if (mode === "tablet") return "平板端";
  return "桌面端";
}
