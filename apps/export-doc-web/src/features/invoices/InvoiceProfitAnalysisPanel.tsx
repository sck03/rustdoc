import { Calculator, ChevronDown, ChevronUp } from "lucide-react";
import { useMemo, useState } from "react";
import {
  ApiInvoiceDetailDto,
  ApiInvoiceProfitAnalysisResponse,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { normalizeInvoiceForSave } from "./invoiceModel.ts";
import { useTransientPreview } from "../../ui/useTransientPreview.ts";

const emptyAnalysis: ApiInvoiceProfitAnalysisResponse = {
  currency: "",
  exchangeRate: "0",
  exchangeRateText: "未设置",
  grossProfit: "0",
  grossProfitText: "¥ 0.00",
  margin: "0",
  marginText: "0.00%",
  purchaseCost: "0",
  purchaseCostText: "- ¥ 0.00",
  salesRmb: "0",
  salesRmbText: "¥ 0.00",
  salesTotal: "0",
  salesTotalText: "0.00",
  storagePolicy: "",
  taxRefund: "0",
  taxRefundText: "+ ¥ 0.00",
};

export function InvoiceProfitAnalysisPanel({
  client,
  invoice,
  invoiceId,
  disabled,
}: {
  client: ExportDocManagerApiClient;
  invoice: ApiInvoiceDetailDto;
  invoiceId: number;
  disabled: boolean;
}) {
  const [isExpanded, setIsExpanded] = useState(false);
  const scope = useMemo(() => ({ client, invoice, invoiceId, disabled }), [client, invoice, invoiceId, disabled]);
  const [failure, setFailure] = useState<{ scope: unknown; text: string } | null>(null);
  const preview = useTransientPreview(scope,
    signal => client.analyzeInvoiceProfit({ body: { invoice: normalizeInvoiceForSave(invoice, invoiceId) } }, { signal }),
    error => setFailure({ scope, text: readApiError(error) }));
  const analysis = preview.preview ?? emptyAnalysis;
  const isBusy = preview.isPending;
  const message = failure?.scope === scope ? failure.text : null;

  const metrics = [
    ["销售总额", analysis.salesTotalText],
    ["汇率", analysis.exchangeRateText],
    ["销售额 RMB", analysis.salesRmbText],
    ["采购成本", analysis.purchaseCostText],
    ["退税收入", analysis.taxRefundText],
    ["预估毛利", analysis.grossProfitText],
    ["毛利率", analysis.marginText],
  ] as const;

  return (
    <section className="form-section profit-analysis-section information-tier-reference" aria-label="利润分析">
      <div className="section-header">
        <div>
          <h3>利润分析</h3>
          <span className="section-description">参考信息，按需展开计算</span>
        </div>
        <div className="toolbar-actions">
          {isExpanded ? (
            <button
              className="command-button secondary"
              type="button"
              onClick={() => { setFailure(null); void preview.generate(); }}
              disabled={disabled || isBusy}
              title="计算利润分析"
            >
              <Calculator size={16} aria-hidden="true" />
              <span>{isBusy ? "计算中" : "计算"}</span>
            </button>
          ) : null}
          <button
            className="secondary-button compact-command-button"
            type="button"
            aria-expanded={isExpanded}
            disabled={isBusy}
            onClick={() => setIsExpanded((current) => !current)}
          >
            {isExpanded ? <ChevronUp size={16} aria-hidden="true" /> : <ChevronDown size={16} aria-hidden="true" />}
            <span>{isExpanded ? "收起利润分析" : "展开利润分析"}</span>
          </button>
        </div>
      </div>

      {isExpanded ? (
        <>
          {message ? <InlineNotice tone="error" title="利润分析失败">{message}</InlineNotice> : null}

          <div className="detail-grid profit-analysis-grid">
            {metrics.map(([label, value]) => (
              <div className="detail-item" key={label}>
                <span>{label}</span>
                <strong>{value}</strong>
              </div>
            ))}
          </div>
        </>
      ) : null}
    </section>
  );
}
