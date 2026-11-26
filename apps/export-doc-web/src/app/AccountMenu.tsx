import { useState } from "react";
import { ChevronDown, KeyRound, LogOut, ShieldCheck } from "lucide-react";
import type { ApiChangePasswordRequest, ApiUserDto } from "../api/index.ts";
import { DropdownMenu } from "../ui/primitives/DropdownMenu.tsx";
import { PermissionSourceSummary } from "./PermissionSourceSummary.tsx";
import { PasswordChangeDialog } from "../features/auth/PasswordChangeDialog.tsx";
import "./account-menu.css";

export type ChangeOwnPassword = (body: ApiChangePasswordRequest, signal: AbortSignal) => Promise<boolean>;

export function AccountMenu({ user, displayName, identityLabel, isDesktopRuntime, disabled, passwordDisabled, onLogout, onChangePassword, onSwitchContext }: {
  user: ApiUserDto; displayName: string; identityLabel: string; isDesktopRuntime: boolean; disabled?: boolean; passwordDisabled?: boolean;
  onLogout: () => void; onChangePassword?: ChangeOwnPassword;
  onSwitchContext?: (id: string) => Promise<void>;
}) {
  const [dialog, setDialog] = useState<"password" | "permissions" | null>(null);
  const contexts = user.unitContexts ?? [];
  return <>
    <DropdownMenu label="账号菜单：" triggerClassName="session-user account-menu-trigger" disabled={disabled}
      trigger={<><span className="session-avatar" aria-hidden="true">{displayName.trim().slice(0, 1).toUpperCase()}</span>
        <span className="session-user-copy"><strong title={displayName}>{displayName}</strong><small>账号 · {identityLabel}</small></span>
        <ChevronDown size={16} aria-hidden="true" /></>}
      groups={contexts.length ? [{ key: "units", label: "单位身份", items: contexts.map(context => ({
        key: context.id, label: `${context.companyName || "未分配单位"} · ${context.name}`,
        checked: context.id === (user.contextId ?? ""), disabled: !onSwitchContext || contexts.length === 1,
        onSelect: () => void onSwitchContext?.(context.id),
      })) }] : []}
      items={[
        ...(onChangePassword ? [{ key: "password", label: "修改密码", disabled: passwordDisabled, icon: <KeyRound size={18} aria-hidden="true" />, onSelect: () => setDialog("password") }] : []),
        ...(user.permissionSource ? [{ key: "permissions", label: "我的权限来源", icon: <ShieldCheck size={18} aria-hidden="true" />, onSelect: () => setDialog("permissions") }] : []),
        { key: "logout", label: "退出登录", icon: <LogOut size={18} aria-hidden="true" />, onSelect: onLogout, danger: true, separator: true },
      ]} />
    {dialog === "permissions" && <PermissionSourceSummary user={user} isDesktopRuntime={isDesktopRuntime} onClose={() => setDialog(null)} />}
    {dialog === "password" && onChangePassword && <PasswordChangeDialog isDesktopRuntime={isDesktopRuntime}
      onChangePassword={onChangePassword} onClose={() => setDialog(null)} />}
  </>;
}
