import { useState } from "react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { ConfirmationDialog } from "../../ui/ConfirmationDialog.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useScopedSave } from "../../ui/useScopedSave.ts";
import { isAbortError } from "../../ui/useAbortableOperation.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import type { MasterDataRecord } from "./masterDataTypes.ts";
import { hsCodeVersionReference } from "./hsCodeMaintenance.ts";

export function HsCodeObsoleteAction({ client, records, disabled, onChanged }: {
  client: ExportDocManagerApiClient;
  records: MasterDataRecord[];
  disabled: boolean;
  onChanged: () => Promise<void>;
}) {
  const [selection, setSelection] = useState<MasterDataRecord[] | null>(null);
  const [reason, setReason] = useState("");
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const save = useScopedSave(records.map(row => `${row.id}:${row.rowVersion}`).join("|"), client);
  useUnsavedChangesGuard({ isDirty: Boolean(selection && reason), message: "确认作废的原因尚未提交。" });
  async function confirmObsolete() {
    if (!selection || disabled || !reason.trim() || save.isSaving) return;
    let saved = false;
    try {
      await save.run(async ({ signal, checkCurrent }) => {
        setError(""); setMessage("");
        const response = await client.confirmHsCodesObsolete({ body: {
          items: selection.map(hsCodeVersionReference), reason: reason.trim(),
        } }, { signal });
        checkCurrent(); saved = true;
        setSelection(null); setReason(""); setMessage(response.message);
        await onChanged();
      });
    } catch (failure) {
      if (!isAbortError(failure)) setError(`${saved ? "已确认作废，但列表刷新失败：" : ""}${readApiError(failure)}`);
    }
  }
  return <div>
    <button className="command-button secondary" type="button" disabled={disabled || save.isSaving || !records.some(row => row.status !== "Obsolete")} onClick={() => {
      setSelection(records.slice()); setReason(""); setError(""); setMessage("");
    }}>{records.length === 1 ? "确认作废" : "批量确认作废"}</button>
    {message && <InlineNotice tone="success">{message}</InlineNotice>}
    {error && <InlineNotice tone="error">{error}</InlineNotice>}
    {selection && <ConfirmationDialog title="确认 HS 编码作废" description={`核对官方税则后，确认这 ${selection.length} 条编码已失效。`}
      details={["编码保留在目录中，最后更新满 12 个日历月后可另行预检清理。", "历史单据、商品、申报实例及替代关系继续保留；已经作废的编码不重置保留期。"]}
      confirmLabel={`确认 ${selection.length} 条作废`} confirmDisabled={!reason.trim()} isBusy={save.isSaving}
      onCancel={() => { setSelection(null); setReason(""); }} onConfirm={() => void confirmObsolete()}>
      <label className="confirmation-dialog-details"><span>确认作废原因</span><textarea aria-label="确认作废原因" rows={3} maxLength={1000} value={reason} disabled={save.isSaving} onChange={event => setReason(event.target.value)} /></label>
    </ConfirmationDialog>}
  </div>;
}
