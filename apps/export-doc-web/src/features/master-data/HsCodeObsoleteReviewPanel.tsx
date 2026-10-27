import { useState } from "react";
import type { ApiHsCodeObsoletePreview, ExportDocManagerApiClient } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useScopedSave } from "../../ui/useScopedSave.ts";
import { isAbortError } from "../../ui/useAbortableOperation.ts";
import { HsCodeMaintenanceTable } from "./HsCodeMaintenanceTable.tsx";
import { HsCodeObsoleteAction } from "./HsCodeObsoleteAction.tsx";
import type { MasterDataRecord } from "./masterDataTypes.ts";

export function HsCodeObsoleteReviewPanel({ client, disabled, businessYear, onChanged }: {
  client: ExportDocManagerApiClient; disabled: boolean; businessYear: number; onChanged: () => Promise<void>;
}) {
  const [preview, setPreview] = useState<ApiHsCodeObsoletePreview | null>(null);
  const [selected, setSelected] = useState<Set<number>>(() => new Set());
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const save = useScopedSave(`obsolete-review:${businessYear}:${disabled}`, client);
  async function load() {
    if (disabled) return;
    try {
      await save.run(async ({ signal, checkCurrent }) => {
        setError(""); setMessage(""); setPreview(null); setSelected(new Set());
        const result = await client.previewHsCodesObsolete({ signal });
        checkCurrent(); setPreview(result);
      });
    } catch (failure) { if (!isAbortError(failure)) setError(readApiError(failure)); }
  }
  return <details className="knowledge-help">
    <summary>疑似作废批量核对</summary>
    <p>先按官方年度税则核实缺失编码，再选择本批或逐条选择并填写确认原因。每批最多 5000 条，可跨页保留选择；确认作废后才开始计算保留期。</p>
    <button className="command-button secondary" type="button" disabled={disabled || save.isSaving} onClick={() => void load()}>读取疑似作废编码</button>
    {error && <InlineNotice tone="error">{error}</InlineNotice>}{message && <InlineNotice tone="success">{message}</InlineNotice>}
    {preview && <div>
      <p>{preview.policy}</p><p>共 {preview.totalCount} 条疑似作废，本批 {preview.items.length} 条，已选 {selected.size} 条。</p>
      <HsCodeMaintenanceTable items={preview.items} busy={disabled || save.isSaving} selectedIds={selected} onSelectionChange={setSelected} />
      <HsCodeObsoleteAction client={client} records={preview.items.filter(row => selected.has(row.id)) as unknown as MasterDataRecord[]} disabled={disabled || save.isSaving} onChanged={async () => {
        setMessage(`已确认本批 ${selected.size} 个编码作废，最后更新满 12 个日历月后可另行预检清理。`);
        setPreview(null); setSelected(new Set());
        try { await onChanged(); } catch (failure) { setError(`已确认作废，但列表刷新失败：${readApiError(failure)}`); }
      }} />
    </div>}
  </details>;
}
