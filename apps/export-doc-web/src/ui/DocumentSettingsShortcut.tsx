import { useNavigate } from "react-router-dom";
import { usePermissionCapabilities } from "../app/PermissionAccessContext.tsx";
import { useConfirmUnsavedChanges } from "./unsavedChangesGuard.tsx";
import type { DocumentFieldGroup } from "../features/settings/settingsNavigationModel.ts";

export function DocumentSettingsShortcut({ group }: { group?: DocumentFieldGroup }) {
  const { canManageSettings } = usePermissionCapabilities();
  return canManageSettings ? <SettingsShortcutButton group={group} /> : null;
}

function SettingsShortcutButton({ group }: { group?: DocumentFieldGroup }) {
  const navigate = useNavigate();
  const confirmDiscardChanges = useConfirmUnsavedChanges();
  const label = group ? "设置字段名称" : "设置录入默认值";
  async function openSettings() {
    if (!await confirmDiscardChanges("打开单据设置")) return;
    navigate(group ? `/settings?section=documentFields&group=${group}` : "/settings?section=documentDefaults");
  }
  return <button className="text-button compact-text-button" type="button" onClick={() => void openSettings()}>{label}</button>;
}
