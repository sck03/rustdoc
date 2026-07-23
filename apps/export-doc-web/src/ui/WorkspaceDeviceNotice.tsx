import type { ReactNode } from "react";
import type { WorkspaceDeviceMode } from "../app/workspaceDevice.ts";
import { getWorkspaceDeviceLabel } from "../app/workspaceDevice.ts";
import { InlineNotice } from "./PageState.tsx";

export function WorkspaceDeviceNotice({
  mode,
  phone,
  tablet,
  className,
}: {
  mode: WorkspaceDeviceMode;
  phone: ReactNode;
  tablet: ReactNode;
  className?: string;
}) {
  if (mode === "desktop") return null;
  return (
    <InlineNotice
      tone="info"
      title={`${getWorkspaceDeviceLabel(mode)}使用范围`}
      className={className ? `workspace-device-notice ${className}` : "workspace-device-notice"}
    >
      {mode === "phone" ? phone : tablet}
    </InlineNotice>
  );
}
