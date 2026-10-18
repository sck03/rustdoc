import { useEffect, useState } from "react";
import { ArrowDown, ArrowUp, ListChecks, Minus, Plus } from "lucide-react";
import { Link } from "react-router-dom";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useExchangeRates } from "../tools/useExchangeRates.ts";
import { exchangeRateSources } from "../tools/exchangeRateSources.ts";
import { formatSettingDateTime } from "./settingsFormatters.ts";
import { normalizeCurrencyList, readStringArray } from "./settingsValueUtils.ts";
import { CheckboxSetting, SelectSetting, TextSetting, readSettingString } from "./SettingsFieldControls.tsx";
import type { SettingPatch, SettingsRecord } from "./settingsTypes.ts";
import { exchangeRateAllSupportedCurrenciesPath } from "./settingsConfigurationPaths.ts";

const exchangeRateSelectedCurrenciesPath = ["exchangeRate", "selectedCurrencies"];
const maxSelectedExchangeCurrencies = 15;
export function ExchangeRateSettingsPanel({ client, settings, canManageSettings, isBusy, onChange, onPatchSettings, onBlocked }: {
  client: ExportDocManagerApiClient;
  settings: SettingsRecord;
  canManageSettings: boolean;
  isBusy: boolean;
  onChange: (path: string[], value: unknown) => void;
  onPatchSettings: (patches: SettingPatch[]) => void;
  onBlocked: (message: string) => void;
}) {
  const quotes = useExchangeRates(client);
  const sourceChanged = readSettingString(settings, ["exchangeRate", "url"]) !== quotes.configuration?.url
    || readSettingString(settings, ["exchangeRate", "sourceFormat"]) !== quotes.configuration?.sourceFormat;
  const minutes = Number(readSettingString(settings, ["exchangeRate", "cacheDurationMinutes"]));
  const intervals = [...new Set([30, 60, 120, 360, 1440, minutes])].filter(value => value > 0).sort((a, b) => a - b);
  return (
    <section className="form-section" aria-label="汇率与币制">
      <div className="section-header">
        <h2>汇率与币制</h2>
        <div className="toolbar-actions">
          <Link className="command-button secondary" to="/tools/exchange-rates">查看今日汇率</Link>
          <button className="command-button secondary" type="button" disabled={isBusy || quotes.isBusy || sourceChanged || !canManageSettings} onClick={quotes.refresh}>
            <ListChecks size={17} aria-hidden="true" /><span>刷新报价与币种</span>
          </button>
        </div>
      </div>
      {quotes.error && <InlineNotice tone="error">{readApiError(quotes.error)}</InlineNotice>}
      {quotes.result?.isStale && <InlineNotice tone="warning">{quotes.result.lastError} 当前保留上次读取的货币列表。</InlineNotice>}
      {sourceChanged && <p className="field-hint">来源有未保存的修改，请先保存设置，再获取新来源的报价与币种。</p>}
      <fieldset className="settings-fieldset" disabled={isBusy || !canManageSettings}>
        <div className="field-grid">
          <SelectSetting settings={settings} path={["exchangeRate", "sourceFormat"]} label="来源格式" options={[...exchangeRateSources, { value: "Auto", label: "自定义 · 自动识别网页 / JSON" }, { value: "Json", label: "自定义 JSON 数据" }]} onChange={(path, value) => {
            const source = exchangeRateSources.find(item => item.value === value);
            onPatchSettings([{ path, value }, ...(source ? [{ path: ["exchangeRate", "url"], value: source.url }] : [])]);
          }} />
          <TextSetting settings={settings} path={["exchangeRate", "url"]} label="汇率源网址" placeholder="https://www.boc.cn/sourcedb/whpj/" maxLength={2048} onChange={onChange} />
          <label><span>获取间隔</span><select aria-label="获取间隔" value={minutes} onChange={event => onChange(["exchangeRate", "cacheDurationMinutes"], Number(event.target.value))}>
            {intervals.map(value => <option key={value} value={value}>{value % 60 === 0 ? `${value / 60} 小时` : `${value} 分钟`}</option>)}
          </select></label>
          <CheckboxSetting settings={settings} path={["exchangeRate", "autoRefreshEnabled"]} label="启用自动获取" onChange={onChange} />
        </div>
        <p className="field-hint">自动获取至少间隔 30 分钟，保存后立即生效，无需保持今日汇率页面打开；桌面需保持程序运行，网页需保持服务器运行。关闭自动获取时，此间隔用于共享缓存。报价不改写单据汇率。</p>
        <ExchangeRateCurrencySettingsPanel settings={settings} availableCurrencies={quotes.result?.availableCurrencies} fetchedAt={quotes.result?.fetchedAt} disabled={isBusy || sourceChanged || !canManageSettings} onPatchSettings={onPatchSettings} onBlocked={onBlocked} />
      </fieldset>
      <details><summary>自定义来源格式与使用说明</summary>
        <p className="field-hint">使用公开 HTTPS 地址，可带普通查询参数，不放置账号、密钥或内网地址。网页格式需提供中行式的命名报价列；JSON 需明确人民币基准、100 外币单位和含时区的发布时间，示例如下。</p>
        <pre className="exchange-source-example">{'{"baseCurrency":"CNY","unit":100,"rates":[{"currencyName":"美元","buyingRate":"710.25","sellingRate":"713.10","middleRate":"711.00","publishTime":"2026-10-17T09:00:00+08:00"}]}'}</pre>
        <p className="field-hint">工商银行和招商银行使用官网公开牌价接口，切换银行后保存即可获取；没有提供的折算价显示为“—”。自定义自动识别仅处理符合上述语义的网页或 JSON，不执行网页脚本，不能判断单位或字段时会报错。</p>
        <p className="field-hint">请遵守来源网站规则。中行牌价仅供参考，实际交易以银行报价为准；未经中国银行许可，不得以商业目的转载其汇率表。自定义来源的货币名称应与常用货币名称一致。</p>
      </details>
    </section>
  );
}

export default ExchangeRateSettingsPanel;

export function ExchangeRateCurrencySettingsPanel({
  settings,
  disabled,
  onPatchSettings,
  onBlocked,
  availableCurrencies: fetchedCurrencies,
  fetchedAt,
}: {
  settings: SettingsRecord;
  disabled: boolean;
  onPatchSettings: (patches: SettingPatch[]) => void;
  onBlocked: (message: string) => void;
  availableCurrencies?: string[];
  fetchedAt?: string | null;
}) {
  const selectedCurrencies = normalizeCurrencyList(readStringArray(settings, exchangeRateSelectedCurrenciesPath));
  const supportedCurrencies = normalizeCurrencyList([
    ...(fetchedCurrencies ?? readStringArray(settings, exchangeRateAllSupportedCurrenciesPath)),
    ...selectedCurrencies,
  ]);
  const selectedCurrencySet = new Set(selectedCurrencies);
  const availableCurrencies = supportedCurrencies.filter((currency) => !selectedCurrencySet.has(currency));
  const [selectedAvailableCurrency, setSelectedAvailableCurrency] = useState("");
  const [selectedCurrency, setSelectedCurrency] = useState("");
  const selectedCurrencyIndex = selectedCurrencies.indexOf(selectedCurrency);
  const canAddCurrency = !disabled && Boolean(selectedAvailableCurrency) && selectedCurrencies.length < maxSelectedExchangeCurrencies;
  const canRemoveCurrency = !disabled && selectedCurrencyIndex >= 0;
  const canMoveCurrencyUp = canRemoveCurrency && selectedCurrencyIndex > 0;
  const canMoveCurrencyDown = canRemoveCurrency && selectedCurrencyIndex < selectedCurrencies.length - 1;

  useEffect(() => {
    if (selectedAvailableCurrency && !availableCurrencies.includes(selectedAvailableCurrency)) {
      setSelectedAvailableCurrency("");
    }
  }, [availableCurrencies, selectedAvailableCurrency]);

  useEffect(() => {
    if (selectedCurrency && !selectedCurrencies.includes(selectedCurrency)) {
      setSelectedCurrency("");
    }
  }, [selectedCurrencies, selectedCurrency]);

  function patchCurrencySettings(nextSelectedCurrencies: string[]) {
    const patches: SettingPatch[] = [
      { path: exchangeRateSelectedCurrenciesPath, value: normalizeCurrencyList(nextSelectedCurrencies) },
    ];

    onPatchSettings(patches);
  }

  function addSelectedCurrency() {
    if (!selectedAvailableCurrency || disabled) {
      return;
    }

    if (selectedCurrencies.length >= maxSelectedExchangeCurrencies) {
      onBlocked(`最多选择 ${maxSelectedExchangeCurrencies} 种常用货币。`);
      return;
    }

    const currency = selectedAvailableCurrency;
    patchCurrencySettings([...selectedCurrencies, currency]);
    setSelectedCurrency(currency);
    setSelectedAvailableCurrency("");
  }

  function removeSelectedCurrency() {
    if (selectedCurrencyIndex < 0 || disabled) {
      return;
    }

    const currency = selectedCurrency;
    patchCurrencySettings(selectedCurrencies.filter((item) => item !== currency));
    setSelectedAvailableCurrency(currency);
    setSelectedCurrency("");
  }

  function moveSelectedCurrency(offset: -1 | 1) {
    if (selectedCurrencyIndex < 0 || disabled) {
      return;
    }

    const nextIndex = selectedCurrencyIndex + offset;
    if (nextIndex < 0 || nextIndex >= selectedCurrencies.length) {
      return;
    }

    const nextCurrencies = [...selectedCurrencies];
    const [currency] = nextCurrencies.splice(selectedCurrencyIndex, 1);
    nextCurrencies.splice(nextIndex, 0, currency);
    patchCurrencySettings(nextCurrencies);
    setSelectedCurrency(currency);
  }

  return (
    <div className="exchange-currency-settings-panel">
      <div className="batch-export-items-toolbar exchange-currency-toolbar">
        <span>{selectedCurrencies.length} / {maxSelectedExchangeCurrencies} 个常用货币</span>
        <span>
          候选 {availableCurrencies.length} 种，最近获取 {formatSettingDateTime(fetchedAt)}
        </span>
      </div>
      <div className="exchange-currency-manager" aria-label="常用币种管理">
        <ExchangeCurrencySelect
          title="可用货币"
          count={availableCurrencies.length}
          value={selectedAvailableCurrency}
          currencies={availableCurrencies}
          disabled={disabled || availableCurrencies.length === 0}
          emptyText="暂无可用货币"
          onChange={setSelectedAvailableCurrency}
          onDoubleClick={addSelectedCurrency}
        />
        <div className="exchange-currency-action-stack" aria-label="货币选择操作">
          <button className="icon-button compact-icon-button" type="button" title="添加到常用货币" aria-label="添加到常用货币" disabled={!canAddCurrency} onClick={addSelectedCurrency}>
            <Plus size={16} aria-hidden="true" />
          </button>
          <button className="icon-button compact-icon-button" type="button" title="移出常用货币" aria-label="移出常用货币" disabled={!canRemoveCurrency} onClick={removeSelectedCurrency}>
            <Minus size={16} aria-hidden="true" />
          </button>
        </div>
        <ExchangeCurrencySelect
          title="常用货币顺序"
          count={selectedCurrencies.length}
          value={selectedCurrency}
          currencies={selectedCurrencies}
          disabled={disabled || selectedCurrencies.length === 0}
          emptyText="暂无常用货币"
          onChange={setSelectedCurrency}
          onDoubleClick={removeSelectedCurrency}
        />
        <div className="exchange-currency-action-stack" aria-label="常用货币排序操作">
          <button className="icon-button compact-icon-button" type="button" title="上移常用货币" aria-label="上移常用货币" disabled={!canMoveCurrencyUp} onClick={() => moveSelectedCurrency(-1)}>
            <ArrowUp size={16} aria-hidden="true" />
          </button>
          <button className="icon-button compact-icon-button" type="button" title="下移常用货币" aria-label="下移常用货币" disabled={!canMoveCurrencyDown} onClick={() => moveSelectedCurrency(1)}>
            <ArrowDown size={16} aria-hidden="true" />
          </button>
        </div>
      </div>
    </div>
  );
}

function ExchangeCurrencySelect({
  title,
  count,
  value,
  currencies,
  disabled,
  emptyText,
  onChange,
  onDoubleClick,
}: {
  title: string;
  count: number;
  value: string;
  currencies: string[];
  disabled: boolean;
  emptyText: string;
  onChange: (value: string) => void;
  onDoubleClick: () => void;
}) {
  return (
    <div className="exchange-currency-list-panel">
      <div className="exchange-currency-list-heading">
        <strong>{title}</strong>
        <span>{count} 种</span>
      </div>
      <select
        className="exchange-currency-select"
        aria-label={title}
        size={7}
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
        onDoubleClick={onDoubleClick}
      >
        {currencies.length === 0 ? (
          <option value="">{emptyText}</option>
        ) : (
          currencies.map((currency) => (
            <option key={currency} value={currency}>
              {currency}
            </option>
          ))
        )}
      </select>
    </div>
  );
}
