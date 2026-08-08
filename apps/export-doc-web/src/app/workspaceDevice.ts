import { useMediaQuery } from "../ui/useMediaQuery.ts";

export type WorkspaceDeviceMode = "phone" | "tablet" | "desktop";

export type WorkspaceDeviceCapabilities = {
  canUseDenseWorkbench: boolean;
  canUseBatchOperations: boolean;
  canImportExport: boolean;
  canUseAdvancedTools: boolean;
};

export type WorkspaceDeviceProfile = {
  mode: WorkspaceDeviceMode;
  hasFinePointer: boolean;
  capabilities: WorkspaceDeviceCapabilities;
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

export function useWorkspaceDeviceProfile(): WorkspaceDeviceProfile {
  const isPhone = useMediaQuery(`(max-width: ${workspacePhoneMaxWidth}px)`);
  const isDesktop = useMediaQuery(`(min-width: ${workspaceDesktopMinWidth}px)`);
  const hasFinePointer = useMediaQuery("(any-pointer: fine) and (any-hover: hover)");
  return resolveWorkspaceDeviceProfile(isPhone, isDesktop, hasFinePointer);
}

export function useWorkspaceDeviceMode(): WorkspaceDeviceMode {
  return useWorkspaceDeviceProfile().mode;
}

export function resolveWorkspaceDeviceMode(
  isPhoneWidth: boolean,
  isDesktopWidth: boolean,
  hasFinePointer: boolean,
): WorkspaceDeviceMode {
  void hasFinePointer;
  if (isPhoneWidth) return "phone";
  if (isDesktopWidth) return "desktop";
  return "tablet";
}

export function resolveWorkspaceDeviceProfile(
  isPhoneWidth: boolean,
  isDesktopWidth: boolean,
  hasFinePointer: boolean,
): WorkspaceDeviceProfile {
  const mode = resolveWorkspaceDeviceMode(isPhoneWidth, isDesktopWidth, hasFinePointer);
  return {
    mode,
    hasFinePointer,
    capabilities: getWorkspaceDeviceCapabilities(mode, hasFinePointer),
  };
}

export function getWorkspaceDeviceCapabilities(
  mode: WorkspaceDeviceMode,
  hasFinePointer = false,
): WorkspaceDeviceCapabilities {
  if (mode !== "tablet" || !hasFinePointer) return capabilities[mode];
  return {
    ...capabilities.tablet,
    canImportExport: true,
    canUseAdvancedTools: true,
  };
}

export function getWorkspaceDeviceLabel(mode: WorkspaceDeviceMode) {
  if (mode === "phone") return "手机端";
  if (mode === "tablet") return "平板端";
  return "桌面端";
}
