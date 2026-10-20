import type { FormEvent } from "react";
import type { ExportDocManagerApiClient, PersonnelRecord } from "../../api/index.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "./OfficeUi.tsx";
import { usePersonnelOperation } from "./usePersonnelData.ts";

export function PersonnelUnlinkAccountDialog({ client, record, onClose }: {
  client: ExportDocManagerApiClient; record: PersonnelRecord; onClose: () => void;
}) {
  const operation = usePersonnelOperation();
  const account = record.account;
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!account) return;
    const reason = String(new FormData(event.currentTarget).get("reason") ?? "").trim();
    void operation.run(signal => client.unlinkPersonnelAccount({ id: record.employee.id, body: {
      expectedVersion: record.versionNumber, userId: account.id, expectedAccountVersion: account.versionNumber, reason,
    } }, { signal }), onClose);
  }
  return <OfficeDialog title={`解除账号绑定 · ${record.employee.fullName}`} onClose={onClose} {...operation} protectChanges>
    <p>解除与“{account?.username}”的绑定。人员档案、账号及历史记录保留，账号启停与权限不变，现有登录需要重新登录。</p>
    <p className="office-muted">有未结申请、审批或办理职责时，请先在“交接事项”中完成交接。解除后可重新关联账号；已有账号关联历史的档案仍需保留追溯。</p>
    <form onSubmit={submit}><fieldset className="office-form-grid" disabled={operation.busy}>
      <OfficeField label="解除原因" wide><textarea name="reason" required maxLength={500} rows={3} autoFocus /></OfficeField>
    </fieldset><OfficeSubmit busy={operation.busy} disabled={!account} label="确认解除绑定" /></form>
  </OfficeDialog>;
}
