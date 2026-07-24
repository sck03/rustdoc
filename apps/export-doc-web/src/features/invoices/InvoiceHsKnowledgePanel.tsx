import { useEffect, useMemo, useState, type FormEvent } from "react";
import { Check, Search, ShieldCheck, X } from "lucide-react";
import type {
  ApiInvoiceItemDto,
  ExportDocManagerApiClient,
  HsCodeKnowledgeSearchItem,
} from "../../api/index.ts";
import { normalizeText, readApiError } from "../../ui/formUtils.ts";
import { buildInvoiceHsQuery } from "./invoiceHsKnowledgeModel.ts";

export function InvoiceHsKnowledgePanel({
  client,
  item,
  open,
  onApply,
  onClose,
}: {
  client: ExportDocManagerApiClient;
  item: ApiInvoiceItemDto | null;
  open: boolean;
  onApply: (patch: Partial<ApiInvoiceItemDto>, result: HsCodeKnowledgeSearchItem) => void;
  onClose: () => void;
}) {
  const suggestedQuery = useMemo(() => buildInvoiceHsQuery(item), [item]);
  const [draft, setDraft] = useState("");
  const [results, setResults] = useState<HsCodeKnowledgeSearchItem[]>([]);
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!open) return;
    setDraft(suggestedQuery);
    setResults([]);
    setMessage(suggestedQuery ? "可直接查询当前明细，也可以补充材质、用途、规格或 HS 编码前缀。" : "请先填写中文品名、英文品名或至少 4 位 HS 编码。");
  }, [open, suggestedQuery]);

  async function search(event?: FormEvent) {
    event?.preventDefault();
    const query = draft.trim();
    if (!query || busy) return;
    setBusy(true);
    setMessage("");
    try {
      const response = await client.searchInvoiceHsCodeKnowledge({ query, maxResults: 20 });
      setResults(response.items);
      setMessage(response.message);
    } catch (error) {
      setResults([]);
      setMessage(readApiError(error));
    } finally {
      setBusy(false);
    }
  }

  async function apply(result: HsCodeKnowledgeSearchItem) {
    if (!result.canUse || !result.currentCode || busy) return;
    setBusy(true);
    setMessage("");
    try {
      const standard = await client.getInvoiceHsCode({ code: result.currentCode });
      if (!isTrustedActiveHsCode(standard)) throw new Error("该编码已不再是经过年度验证的当前有效编码，请重新查询。");
      try {
        await client.recordInvoiceHsCodeKnowledgeFeedback({
          body: {
            queryText: draft.trim(),
            productName: result.name,
            specification: result.specification,
            candidateCode: result.currentCode,
            accepted: true,
          },
        });
      } catch {
        // Feedback improves future ranking but must not block the user's explicit invoice fill action.
      }
      const patch: Partial<ApiInvoiceItemDto> = { hsCode: result.currentCode };
      if (!normalizeText(item?.unitCN) && normalizeText(standard.unit)) patch.unitCN = normalizeText(standard.unit);
      if (!(item?.taxRebateRate ?? 0)) {
        const rate = parsePercent(standard.rebateRate);
        if (rate != null) patch.taxRebateRate = rate;
      }
      onApply(patch, result);
      onClose();
    } catch (error) {
      setMessage(readApiError(error));
    } finally {
      setBusy(false);
    }
  }

  if (!open) return null;
  return (
    <div className="invoice-hs-panel-backdrop" role="presentation" onMouseDown={(event) => event.target === event.currentTarget && onClose()}>
      <aside className="invoice-hs-panel" role="dialog" aria-modal="true" aria-labelledby="invoice-hs-panel-title">
        <header>
          <div>
            <span className="eyebrow">本地已审核知识</span>
            <h2 id="invoice-hs-panel-title">智能匹配 HS 编码</h2>
            <p>只允许回填带年度、来源和验证时间的当前有效编码。</p>
          </div>
          <button className="icon-button" type="button" title="关闭" aria-label="关闭" onClick={onClose}><X size={18}/></button>
        </header>
        <form className="invoice-hs-search" onSubmit={search}>
          <textarea value={draft} onChange={(event) => setDraft(event.target.value)} placeholder="商品名称、材质、用途、规格或至少 4 位 HS 编码" />
          <button className="command-button" type="submit" disabled={busy || !draft.trim()}><Search size={16}/>{busy ? "查询中" : "查询"}</button>
        </form>
        {message ? <div className="invoice-hs-message">{message}</div> : null}
        <div className="invoice-hs-results">
          {results.map((result) => (
            <article key={`${result.currentCode}-${result.rawCode}`} className={result.canUse ? "usable" : "warning"}>
              <div className="invoice-hs-result-main">
                <div className="invoice-hs-result-title"><strong>{result.currentCode || result.rawCode}</strong><span>{result.standardName || result.name}</span></div>
                <p>{result.specification || result.name}</p>
                <div className="invoice-hs-provenance"><ShieldCheck size={14}/><span>{result.standardSource || "来源未标明"}</span><span>{result.effectiveYear ? `${result.effectiveYear} 年` : "年度未标明"}</span><span>{formatDate(result.lastVerifiedAt)}</span></div>
                {result.conflictWarnings.map((warning) => <small className="conflict" key={warning}>{warning}</small>)}
              </div>
              <div className="invoice-hs-result-action">
                <b>{result.score}</b><small>{result.exampleCount} 条实例</small>
                <button className="command-button" type="button" disabled={!result.canUse || busy} onClick={() => void apply(result)}><Check size={15}/>回填当前行</button>
              </div>
            </article>
          ))}
        </div>
      </aside>
    </div>
  );
}

function parsePercent(value?: string) {
  const number = Number.parseFloat((value ?? "").replace("%", "").trim());
  return Number.isFinite(number) ? number : null;
}

function isTrustedActiveHsCode(value: { status?: string; sourceName?: string; effectiveYear?: number; lastVerifiedAt?: string }) {
  return value.status === "Active" && !!value.sourceName?.trim() && !!value.lastVerifiedAt &&
    (value.effectiveYear ?? 0) >= 2000 && (value.effectiveYear ?? 0) <= 2100;
}

function formatDate(value?: string) {
  if (!value) return "未标明验证时间";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : `验证于 ${date.toLocaleDateString("zh-CN")}`;
}
