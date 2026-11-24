import { useState, type FormEvent } from "react";
import { Dialog } from "../../ui/primitives/Dialog.tsx";
import { Field, FormGrid } from "../../ui/primitives/Field.tsx";
import { Button } from "../../ui/Button.tsx";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import { isComposingKey } from "../../ui/keyboardEvent.ts";
import { readApiError } from "../../ui/formUtils.ts";
import type { ChangeOwnPassword } from "../../app/AccountMenu.tsx";

export function PasswordChangeDialog({ onChangePassword, onClose, isDesktopRuntime }: {
  onChangePassword: ChangeOwnPassword; onClose: () => void; isDesktopRuntime: boolean;
}) {
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const run = useAbortableOperation();
  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    const length = Array.from(newPassword).length;
    if (length < 8 || length > 128) { setError("新密码须为 8–128 个字符。"); return; }
    if (newPassword !== confirmation) { setError("两次输入的新密码不一致。"); return; }
    if (newPassword === currentPassword) { setError("新密码不能与原密码相同。"); return; }
    setBusy(true); setError("");
    try {
      const changed = await run(signal => onChangePassword({ currentPassword, newPassword }, signal));
      if (changed) { setCurrentPassword(""); setNewPassword(""); setConfirmation(""); onClose(); }
      else setBusy(false);
    } catch (failure) {
      if (!isAbortError(failure)) { setError(readApiError(failure)); setBusy(false); }
    }
  }
  return <Dialog title="修改密码" size="small" busy={busy} error={error} onClose={onClose}
    description="修改成功后，请使用新密码重新登录。">
    <form onSubmit={event => void submit(event)} onKeyDown={event => {
      if (event.key === "Enter" && isComposingKey(event.nativeEvent)) event.preventDefault();
    }}>
      <FormGrid columns={1} disabled={busy}>
        <Field label="原密码" description={isDesktopRuntime ? "初始密码为空时可留空。" : undefined}>
          <input type="password" autoComplete="current-password" required={!isDesktopRuntime} maxLength={256}
            value={currentPassword} onChange={event => setCurrentPassword(event.target.value)} />
        </Field>
        <Field label="新密码" description="8–128 个字符。">
          <input type="password" autoComplete="new-password" required maxLength={256}
            value={newPassword} onChange={event => setNewPassword(event.target.value)} />
        </Field>
        <Field label="确认新密码"><input type="password" autoComplete="new-password" required maxLength={256}
          value={confirmation} onChange={event => setConfirmation(event.target.value)} /></Field>
      </FormGrid>
      <footer className="office-form-actions"><Button type="button" disabled={busy} onClick={onClose}>取消</Button>
        <Button variant="primary" type="submit" busy={busy}>修改并重新登录</Button></footer>
    </form>
  </Dialog>;
}
