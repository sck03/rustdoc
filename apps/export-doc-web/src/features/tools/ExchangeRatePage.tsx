import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { ListChecks, RefreshCw, RotateCcw } from "lucide-react";
import {
  type ApiExchangeRateDto,
  type ApiExchangeRateListResponse,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";

export function ExchangeRatePage({ client }: { client: ExportDocManagerApiClient }) {
  const [ratesResult, setRatesResult] = useState<ApiExchangeRateListResponse | null>(null);
  const [availableCurrencies, setAvailableCurrencies] = useState<string[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [messageType, setMessageType] = useState<"success" | "error">("success");

  const ratesMutation = useMutation({
    mutationFn: (forceRefresh: boolean) => client.listExchangeRates({ forceRefresh }),
    onSuccess: (response) => {
      setRatesResult(response);
      setMessage(response.statusText || "汇率已更新。");
      setMessageType(response.rates.length > 0 ? "success" : "error");
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setMessageType("error");
    },
  });

  const currenciesMutation = useMutation({
    mutationFn: () => client.listAvailableExchangeRateCurrencies(),
    onSuccess: (response) => {
      setAvailableCurrencies(response.currencies ?? []);
      setMessage(`已读取 ${response.currencies?.length ?? 0} 种可用货币。`);
      setMessageType((response.currencies?.length ?? 0) > 0 ? "success" : "error");
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setMessageType("error");
    },
  });

  const isBusy = ratesMutation.isPending || currenciesMutation.isPending;
  const rates = ratesResult?.rates ?? [];
  const displayedCurrencies = availableCurrencies.length
    ? availableCurrencies
    : normalizeCurrencyList([
        ...(ratesResult?.selectedCurrencies ?? []),
        ...rates.map((rate) => rate.currencyName),
      ]);

  function refreshRates(forceRefresh: boolean) {
    setMessage(null);
    ratesMutation.mutate(forceRefresh);
  }

  function loadAvailableCurrencies() {
    setMessage(null);
    currenciesMutation.mutate();
  }

  return (
    <section className="work-surface exchange-rate-surface" aria-label="今日汇率">
      <div className="toolbar exchange-rate-toolbar">
        <div className="toolbar-summary">
          <strong>{ratesResult ? `${rates.length} 种货币` : "未获取"}</strong>
          <span>{ratesResult?.sourceUrl || "中国银行汇率源"}</span>
        </div>
        <div className="toolbar-actions">
          <button className="icon-button" type="button" title="读取可用货币" disabled={isBusy} onClick={loadAvailableCurrencies}>
            <ListChecks size={18} aria-hidden="true" />
          </button>
          <button className="icon-button" type="button" title="刷新汇率" disabled={isBusy} onClick={() => refreshRates(false)}>
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          <button className="icon-button solid" type="button" title="强制刷新汇率" disabled={isBusy} onClick={() => refreshRates(true)}>
            <RotateCcw size={18} aria-hidden="true" />
          </button>
        </div>
      </div>

      {message ? <div className={messageType === "error" ? "alert" : "success-alert"}>{message}</div> : null}

      <section className="form-section" aria-label="汇率状态">
        <div className="detail-grid exchange-rate-detail-grid">
          <DetailItem label="汇率源" value={ratesResult?.sourceUrl || "-"} wide />
          <DetailItem label="缓存分钟" value={ratesResult ? String(ratesResult.cacheDurationMinutes) : "-"} />
          <DetailItem label="更新时间" value={formatDateTime(ratesResult?.fetchedAt)} />
          <DetailItem label="常用货币" value={(ratesResult?.selectedCurrencies ?? []).join(" / ") || "-"} wide />
        </div>
      </section>

      <div className="exchange-rate-layout">
        <section className="form-section exchange-rate-table-section" aria-label="汇率列表">
          <div className="section-header">
            <div>
              <h2>汇率列表</h2>
              <span>{ratesResult?.statusText || "等待获取"}</span>
            </div>
          </div>
          <div className="table-frame exchange-rate-table-frame">
            <table className="exchange-rate-table">
              <thead>
                <tr>
                  <th>货币名称</th>
                  <th>现汇买入价</th>
                  <th>现钞买入价</th>
                  <th>现汇卖出价</th>
                  <th>现钞卖出价</th>
                  <th>中行折算价</th>
                  <th>发布时间</th>
                </tr>
              </thead>
              <tbody>
                {rates.map((rate) => (
                  <ExchangeRateRow key={`${rate.currencyName}-${rate.publishTime}`} rate={rate} />
                ))}
                {!rates.length ? (
                  <tr>
                    <td className="empty-cell small-empty" colSpan={7}>
                      {isBusy ? "正在获取汇率" : "暂无汇率数据"}
                    </td>
                  </tr>
                ) : null}
              </tbody>
            </table>
          </div>
        </section>

        <section className="form-section exchange-rate-currency-section" aria-label="可用货币">
          <div className="section-header">
            <div>
              <h2>可用货币</h2>
              <span>{displayedCurrencies.length ? `${displayedCurrencies.length} 种` : "未读取"}</span>
            </div>
          </div>
          <div className="currency-chip-list">
            {displayedCurrencies.map((currency) => (
              <span key={currency}>{currency}</span>
            ))}
            {!displayedCurrencies.length ? <div className="empty-cell small-empty">暂无货币清单</div> : null}
          </div>
        </section>
      </div>
    </section>
  );
}

function normalizeCurrencyList(currencies: string[]) {
  const normalized: string[] = [];
  const seen = new Set<string>();

  for (const currency of currencies) {
    const value = currency.trim();
    if (!value || seen.has(value)) {
      continue;
    }

    seen.add(value);
    normalized.push(value);
  }

  return normalized;
}

function ExchangeRateRow({ rate }: { rate: ApiExchangeRateDto }) {
  return (
    <tr>
      <td>{rate.currencyName || "-"}</td>
      <td>{formatRate(rate.buyingRate)}</td>
      <td>{formatRate(rate.cashBuyingRate)}</td>
      <td>{formatRate(rate.sellingRate)}</td>
      <td>{formatRate(rate.cashSellingRate)}</td>
      <td>{formatRate(rate.middleRate)}</td>
      <td>{rate.publishTime || "-"}</td>
    </tr>
  );
}

function DetailItem({ label, value, wide }: { label: string; value: string; wide?: boolean }) {
  return (
    <div className={wide ? "detail-item detail-item-wide" : "detail-item"}>
      <span>{label}</span>
      <strong title={value}>{value}</strong>
    </div>
  );
}

function formatRate(value?: number | null) {
  return typeof value === "number" && Number.isFinite(value) ? value.toFixed(4) : "-";
}

function formatDateTime(value?: string) {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}
