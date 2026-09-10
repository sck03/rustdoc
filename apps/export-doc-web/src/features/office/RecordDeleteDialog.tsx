import type { FormEvent } from "react";
import type { DeleteRecordRequest } from "../../api/index.ts";
import { OfficeDialog, OfficeField } from "./OfficeUi.tsx";
import type { useOfficeOperation } from "./useOfficeData.ts";

export function RecordDeleteDialog({ name, description, version, operation, onDelete, onClose, onDeleted }: {
  name: string; description: string; version: number; operation: ReturnType<typeof useOfficeOperation>;
  onDelete: (body: DeleteRecordRequest, signal: AbortSignal) => Promise<unknown>; onClose: () => void; onDeleted: () => void;
}) {
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const reason = String(new FormData(event.currentTarget).get("reason") ?? "").trim();
    void operation.run((signal) => onDelete({ expectedVersion: version, reason }, signal), onDeleted);
  }
  return <OfficeDialog title={`删除 · ${name}`} onClose={onClose} {...operation} protectChanges>
    <p>{description}</p>
    <form onSubmit={submit}><fieldset className="office-form-grid" disabled={operation.busy}>
      <OfficeField label="删除原因" wide><textarea name="reason" required rows={3} maxLength={500} autoFocus /></OfficeField>
      <label className="checkbox-field office-field-wide"><input type="checkbox" required />确认删除“{name}”，此操作无法撤销</label>
    </fieldset><footer className="office-form-actions"><button type="button" className="command-button secondary" disabled={operation.busy} onClick={onClose}>保留记录</button>
      <button type="submit" className="command-button danger" disabled={operation.busy}>{operation.busy ? "正在删除…" : "确认删除"}</button>
    </footer></form>
  </OfficeDialog>;
}
