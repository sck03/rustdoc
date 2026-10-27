import { useState } from "react";
import type { ApiHsCodeCleanupPreview, ExportDocManagerApiClient } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { isAbortError } from "../../ui/useAbortableOperation.ts";
import { useScopedSave } from "../../ui/useScopedSave.ts";
import { HsCodeMaintenanceTable } from "./HsCodeMaintenanceTable.tsx";

export function HsCodeCleanupPanel({ client, disabled, onChanged }: { client: ExportDocManagerApiClient; disabled: boolean; onChanged: () => Promise<void> }) {
  const [preview, setPreview] = useState<ApiHsCodeCleanupPreview | null>(null);
  const [message, setMessage] = useState("");
  const save = useScopedSave(`hs-cleanup:${disabled}`, client);
  const [error, setError] = useState("");
  const action = { isPending: save.isSaving, error, run: async (operation: (signal: AbortSignal) => Promise<void>) => {
    if (disabled) return;
    setError("");
    try { await save.run(async ({ signal }) => { await operation(signal); signal.throwIfAborted(); }); }
    catch (failure) { if (!isAbortError(failure)) setError(readApiError(failure)); }
  } };
  return <details className="knowledge-help">
    <summary>失效编码清理 · 保留一年过渡</summary>
    <p>完整年度导入只将缺失编码标记为“疑似作废”。核对官方税则后，在编码详情使用“确认作废”，或在目录多选后“批量确认作废”并填写原因；最后更新满 12 个日历月后才能通过此处清理。历史单据、商品、申报实例和替代关系保留。</p>
    <button type="button" className="command-button secondary" disabled={disabled || action.isPending} onClick={() => void action.run(async signal => {
      setPreview(null); setMessage("");
      const result = await client.previewHsCodeCleanup({ signal });
      if (signal.aborted) return;
      setPreview(result); setMessage("");
    })}>预检可清理编码</button>
    {action.error && <InlineNotice tone="error">{readApiError(action.error)}</InlineNotice>}
    {message && <InlineNotice tone="success">{message}</InlineNotice>}
    {preview && <div>
      <p>{preview.policy}</p><p>本批可清理 {preview.eligibleCount} 条，保留 {preview.retainedCount} 条。</p>
      <HsCodeMaintenanceTable items={preview.items} busy={disabled || action.isPending} />
      <button type="button" className="command-button secondary danger" disabled={disabled || action.isPending || !preview.eligibleCount} onClick={() => void action.run(async signal => {
        const result = await client.commitHsCodeCleanup({ body: { token: preview.token } }, { signal });
        if (signal.aborted) return;
        setPreview(null); setMessage(result.message); await onChanged();
      })}>确认清理本批 {preview.eligibleCount} 条</button>
    </div>}
  </details>;
}
