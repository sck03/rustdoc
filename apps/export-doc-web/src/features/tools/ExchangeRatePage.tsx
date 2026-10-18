import { RefreshCw, Settings } from "lucide-react";
import { Link } from "react-router-dom";
import type { ApiExchangeRateDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { formatSettingDateTime } from "../settings/settingsFormatters.ts";
import { useExchangeRates } from "./useExchangeRates.ts";
import { exchangeRateSourceName } from "./exchangeRateSources.ts";

export function ExchangeRatePage({ client, canManageSettings = false }: { client: ExportDocManagerApiClient; canManageSettings?: boolean }) {
  const { result, configuration, isBusy, error, refresh } = useExchangeRates(client);
  const rates = result?.rates ?? [];
  const automatic = result?.autoRefreshEnabled ?? configuration?.autoRefreshEnabled;
  const source = result?.sourceUrl || configuration?.url;
  return (
    <section className="work-surface exchange-rate-surface" aria-label="今日汇率">
      <div className="toolbar exchange-rate-toolbar">
        <div className="toolbar-summary">
          <strong>今日汇率 · {result ? `${rates.length} 种常用货币` : "读取中"}</strong>
          <span>每 100 外币折算人民币（CNY），仅供参考</span>
        </div>
        <div className="toolbar-actions">
          {canManageSettings && <Link className="command-button secondary" to="/settings?section=exchangeRate"><Settings size={17} aria-hidden="true" />币制与汇率设置</Link>}
          <button className="command-button solid" type="button" disabled={isBusy} onClick={refresh}>
            <RefreshCw size={17} aria-hidden="true" />{isBusy ? "正在读取" : "刷新汇率"}
          </button>
        </div>
      </div>
      {error && <InlineNotice tone="error">{readApiError(error)}{result ? " 当前保留上次报价。" : " 请检查汇率设置或稍后重试。"}</InlineNotice>}
      {result?.isStale && <InlineNotice tone="warning">{result.lastError || "报价缓存已到期。"} 显示上次成功结果，请核对发布时间。</InlineNotice>}
      {Boolean(result?.missingCurrencies?.length) && <InlineNotice tone="warning">来源暂未提供：{result?.missingCurrencies?.join("、")}。常用货币选择仍然保留。</InlineNotice>}
      <section className="form-section" aria-label="汇率状态">
        <div className="detail-grid exchange-rate-detail-grid">
          <DetailItem label="自动获取" value={automatic ? `每 ${configuration?.cacheDurationMinutes ?? result?.cacheDurationMinutes} 分钟` : "已关闭 · 手动获取"} />
          <DetailItem label="最近获取" value={formatSettingDateTime(result?.fetchedAt)} />
          <DetailItem label="下次自动获取" value={automatic ? formatSettingDateTime(result?.nextRefreshAt) : "未开启"} />
          <DetailItem label="来源" value={exchangeRateSourceName(result?.sourceFormat ?? configuration?.sourceFormat, source)} />
        </div>
        <p className="field-hint">{automatic ? "程序或服务器运行时按计划获取，关闭此页面仍会继续。" : "进入页面时读取共享报价，可手动刷新；管理员可在设置中开启定时获取。"} 连续刷新一分钟内复用结果，来源失败后自动延长重试间隔。</p>
      </section>
      <section className="form-section exchange-rate-table-section" aria-label="汇率列表">
        <div className="section-header"><h2>常用货币报价</h2><span>{result?.statusText || (error ? "获取失败" : "正在读取")}</span></div>
        <ResponsiveTableFrame className="exchange-rate-table-frame" label="汇率查询结果">
          <table className="exchange-rate-table">
            <thead><tr><th>货币名称</th><th>现汇买入价</th><th>现钞买入价</th><th>现汇卖出价</th><th>现钞卖出价</th><th>折算价</th><th>来源发布时间</th></tr></thead>
            <tbody>
              {rates.map(rate => <ExchangeRateRow key={rate.currencyName} rate={rate} />)}
              {!rates.length && <tr><td className="empty-cell small-empty" colSpan={7}>{isBusy ? "正在获取汇率" : result && !result.selectedCurrencies.length ? "尚未选择常用货币，请在币制与汇率设置中添加。" : "暂无可用报价"}</td></tr>}
            </tbody>
          </table>
        </ResponsiveTableFrame>
      </section>
      <details className="form-section"><summary>来源说明与可用货币</summary>
        <p className="field-hint">{result?.storagePolicy || "报价不自动改写已保存单据中的汇率。"}</p>
        {source && <p><a href={source} target="_blank" rel="noreferrer">查看原始来源</a></p>}
        {(result?.sourceFormat ?? configuration?.sourceFormat) === "BankOfChinaHtml" && <p className="field-hint">中行牌价仅供参考，实际结购汇以银行交易报价为准。使用前请遵守来源网站规则；未经中国银行许可，不得以商业目的转载其汇率表。</p>}
        <div className="currency-chip-list">{result?.availableCurrencies?.map(currency => <span key={currency}>{currency}</span>)}</div>
      </details>
    </section>
  );
}

function ExchangeRateRow({ rate }: { rate: ApiExchangeRateDto }) {
  const formatRate = (value?: number | null) => value == null ? "—" : value.toLocaleString("zh-CN", { minimumFractionDigits: 4, maximumFractionDigits: 12, useGrouping: false });
  return <tr><td>{rate.currencyName}</td><td>{formatRate(rate.buyingRate)}</td><td>{formatRate(rate.cashBuyingRate)}</td><td>{formatRate(rate.sellingRate)}</td><td>{formatRate(rate.cashSellingRate)}</td><td>{formatRate(rate.middleRate)}</td><td title={rate.publishTime}>{formatSettingDateTime(rate.publishTime)}</td></tr>;
}
function DetailItem({ label, value }: { label: string; value: string }) {
  return <div className="detail-item"><span>{label}</span><strong title={value}>{value}</strong></div>;
}
