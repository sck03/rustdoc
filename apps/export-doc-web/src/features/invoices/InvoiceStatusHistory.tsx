import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { getInvoiceStatusLabel } from "./invoiceModel.ts";

export function InvoiceStatusHistory({ client, invoiceId }: { client: ExportDocManagerApiClient; invoiceId: number }) {
  const [open, setOpen] = useState(false);
  const [cursors, setCursors] = useState<number[]>([]);
  const beforeId = cursors.at(-1);
  const query = useQuery({
    queryKey: [...queryKeys.invoiceStatusHistory(invoiceId), beforeId ?? null],
    queryFn: ({ signal }) => client.listInvoiceStatusHistory({ id: invoiceId, beforeId, limit: 50 }, { signal }),
    enabled: open && invoiceId > 0,
  });
  if (invoiceId <= 0) return null;
  return <details className="form-section" onToggle={event => setOpen(event.currentTarget.open)}>
    <summary>状态记录</summary>
    {query.isFetching && <p role="status">正在加载状态记录…</p>}
    {query.isError && <InlineNotice tone="warning">{readApiError(query.error)}</InlineNotice>}
    {query.data?.items.length === 0 && <p>暂无状态记录</p>}
    <ol className="invoice-status-history-list">
      {query.data?.items.map(entry => <li key={entry.id}>
        <div><strong>{getInvoiceStatusLabel(entry.fromStatus)} → {getInvoiceStatusLabel(entry.toStatus)}</strong><span>{entry.note || "未填写备注"}</span></div>
        <small>{entry.changedByUsername} · {new Date(entry.changedAt).toLocaleString("zh-CN", { hour12: false })}</small>
      </li>)}
    </ol>
    {(cursors.length > 0 || query.data?.nextCursor != null) && <div className="toolbar-actions">
      <button type="button" className="secondary-button" disabled={!cursors.length || query.isFetching} onClick={() => setCursors(value => value.slice(0, -1))}>上一页</button>
      <button type="button" className="secondary-button" disabled={query.data?.nextCursor == null || query.isFetching} onClick={() => { const next = query.data?.nextCursor; if (next != null) setCursors(value => [...value, next]); }}>下一页</button>
    </div>}
  </details>;
}
