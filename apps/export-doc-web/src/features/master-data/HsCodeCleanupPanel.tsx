import { useRef, useState } from "react";
import type { ApiHsCodeCleanupPreview, ExportDocManagerApiClient } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";

export function HsCodeCleanupPanel({ client, disabled, onChanged }: { client: ExportDocManagerApiClient; disabled: boolean; onChanged: () => Promise<void> }) {
  const [preview, setPreview] = useState<ApiHsCodeCleanupPreview | null>(null);
  const [message, setMessage] = useState("");
  const abortable = useAbortableOperation(disabled);
  const inFlight = useRef(false);
  const [isPending, setPending] = useState(false);
  const [error, setError] = useState("");
  const action = { isPending, error, run: async (operation: (signal: AbortSignal) => Promise<void>) => {
    if (disabled || inFlight.current) return;
    inFlight.current = true; setPending(true); setError("");
    try { await abortable(operation); }
    catch (failure) { if (!isAbortError(failure)) setError(readApiError(failure)); }
    finally { inFlight.current = false; setPending(false); }
  } };
  return <details className="knowledge-help">
    <summary>失效编码清理 · 保留一年过渡</summary>
    <p>完整年度导入只将缺失编码标记为“疑似作废”。核对官方税则后，可在编码详情中改为“已作废”；最后更新满一年后才能通过此处清理。历史单据、商品、申报实例和替代关系保留。</p>
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
      <div className="exchange-currency-list-panel" style={{ maxHeight: 240, overflow: "auto" }}>
        {preview.items.slice(0, 100).map(item => <p key={item.id}>{item.code} · {item.name} · 最后更新 {item.updateTime}</p>)}
        {preview.items.length > 100 && <p>仅展示前 100 条，本批共 {preview.items.length} 条。</p>}
      </div>
      <button type="button" className="command-button secondary danger" disabled={disabled || action.isPending || !preview.eligibleCount} onClick={() => void action.run(async signal => {
        const result = await client.commitHsCodeCleanup({ body: { token: preview.token } }, { signal });
        if (signal.aborted) return;
        setPreview(null); setMessage(result.message); await onChanged();
      })}>确认清理本批 {preview.eligibleCount} 条</button>
    </div>}
  </details>;
}
