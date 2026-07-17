import { useMutation } from "@tanstack/react-query";
import { Calculator } from "lucide-react";
import {
  ApiInvoiceDetailDto,
  ApiInvoiceProfitAnalysisResponse,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { normalizeInvoiceForSave } from "./invoiceModel.ts";

const emptyAnalysis: ApiInvoiceProfitAnalysisResponse = {
  currency: "",
  exchangeRate: 0,
  exchangeRateText: "未设置",
  grossProfit: 0,
  grossProfitText: "¥ 0.00",
  margin: 0,
  marginText: "0.00%",
  purchaseCost: 0,
  purchaseCostText: "- ¥ 0.00",
  salesRmb: 0,
  salesRmbText: "¥ 0.00",
  salesTotal: 0,
  salesTotalText: "0.00",
  storagePolicy: "",
  taxRefund: 0,
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
  const profitMutation = useMutation({
    mutationFn: () =>
      client.analyzeInvoiceProfit({
        body: {
          invoice: normalizeInvoiceForSave(invoice, invoiceId),
        },
      }),
  });

  const analysis = profitMutation.data ?? emptyAnalysis;
  const isBusy = profitMutation.isPending;
  const message = profitMutation.isError ? readApiError(profitMutation.error) : null;

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
    <section className="form-section profit-analysis-section" aria-label="利润分析">
      <div className="section-header">
        <h3>利润分析</h3>
        <button
          className="command-button secondary"
          type="button"
          onClick={() => profitMutation.mutate()}
          disabled={disabled || isBusy}
          title="计算利润分析"
        >
          <Calculator size={16} aria-hidden="true" />
          <span>{isBusy ? "计算中" : "计算"}</span>
        </button>
      </div>

      {message ? <div className="alert">{message}</div> : null}

      <div className="detail-grid profit-analysis-grid">
        {metrics.map(([label, value]) => (
          <div className="detail-item" key={label}>
            <span>{label}</span>
            <strong>{value}</strong>
          </div>
        ))}
      </div>
    </section>
  );
}
