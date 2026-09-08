import { useState } from "react";
import type { ApiInvoiceDetailDto, ExportDocManagerApiClient, InvoiceReviewResult } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { invoiceReviewIssueLabel, prepareInvoiceReview } from "./invoiceReviewModel.ts";

export function InvoiceReviewPanel({ client, invoice, disabled, hasUnsavedChanges }: {
  client: ExportDocManagerApiClient; invoice: ApiInvoiceDetailDto; disabled: boolean; hasUnsavedChanges: boolean;
}) {
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<{ snapshot: ApiInvoiceDetailDto; sourceRows: number[]; review?: InvoiceReviewResult; error?: string } | null>(null);
  const run = useAbortableOperation();
  async function check() {
    if (busy || disabled) return;
    setBusy(true);
    try {
      const input = prepareInvoiceReview(invoice);
      const review = await run((signal) => client.reviewInvoice({ body: input.body }, { signal }));
      setResult({ snapshot: invoice, sourceRows: input.sourceRows, review });
    } catch (error) {
      if (!isAbortError(error)) setResult({ snapshot: invoice, sourceRows: [], error: readApiError(error) });
    } finally { setBusy(false); }
  }
  const current = result?.snapshot === invoice ? result : null;
  return <details className="form-section" aria-label="单据核对检查">
    <summary>核对检查</summary>
    <p className="form-field-description">草稿可先保存，核对前补齐必要资料。检查的是当前页面内容；{hasUnsavedChanges ? "当前有未保存修改，提交核对前请先保存。" : "正式核对使用已保存的数据。"}</p>
    <button type="button" className="command-button secondary" disabled={busy || disabled} onClick={() => void check()}>{busy ? "正在检查…" : "检查当前单据"}</button>
    {current?.error && <InlineNotice tone="error">{current.error}</InlineNotice>}
    {current?.review?.ready && <InlineNotice tone="success">必要字段检查通过。请再确认客户原始资料、价格与实际交付要求。</InlineNotice>}
    {current?.review && !current.review.ready && <InlineNotice tone="warning" title={`有 ${current.review.issues.length} 项需要完善`}>
      <ol>{current.review.issues.slice(0, 50).map((issue, index) => <li key={`${issue.field}:${issue.rowNumber}:${index}`}>{invoiceReviewIssueLabel(issue, current.sourceRows)}</li>)}</ol>
      {current.review.issues.length > 50 && <p>当前显示前 50 项，修正后可重新检查。</p>}
    </InlineNotice>}
  </details>;
}
