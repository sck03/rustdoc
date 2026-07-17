import { useEffect, useState } from "react";
import { ArrowDown, ArrowUp, ListChecks, Minus, Plus } from "lucide-react";
import { formatSettingDateTime } from "./settingsFormatters.ts";
import { normalizeCurrencyList, readStringArray } from "./settingsValueUtils.ts";
import { NumberSetting, TextSetting, readSettingString } from "./SettingsFieldControls.tsx";
import type { SettingPatch, SettingsRecord } from "./settingsTypes.ts";
import { exchangeRateAllSupportedCurrenciesPath, exchangeRateLastCurrencyListUpdateTimePath } from "./settingsConfigurationPaths.ts";

const exchangeRateSelectedCurrenciesPath = ["exchangeRate", "selectedCurrencies"];
const maxSelectedExchangeCurrencies = 15;
const defaultExchangeCurrencies = [
  "美元", "欧元", "日元", "英镑", "港币", "澳大利亚元", "加拿大元", "瑞士法郎",
  "新加坡元", "新西兰元", "韩国元", "泰国铢", "卢布", "澳门元", "林吉特",
];

export function ExchangeRateSettingsPanel({ settings, canManageSettings, isBusy, onChange, onPatchSettings, onBlocked, onRefreshCurrencies }: {
  settings: SettingsRecord;
  canManageSettings: boolean;
  isBusy: boolean;
  onChange: (path: string[], value: unknown) => void;
  onPatchSettings: (patches: SettingPatch[]) => void;
  onBlocked: (message: string) => void;
  onRefreshCurrencies: () => void;
}) {
  return (
    <section className="form-section" aria-label="汇率与币制">
      <div className="section-header">
        <h2>汇率与币制</h2>
        <div className="toolbar-actions">
          <button className="command-button secondary" type="button" disabled={isBusy || !canManageSettings} onClick={onRefreshCurrencies}>
            <ListChecks size={17} aria-hidden="true" /><span>更新货币列表</span>
          </button>
        </div>
      </div>
      <fieldset className="settings-fieldset" disabled={!canManageSettings}>
        <div className="field-grid">
          <TextSetting settings={settings} path={["exchangeRate", "url"]} label="汇率源网址" onChange={onChange} />
          <NumberSetting settings={settings} path={["exchangeRate", "cacheDurationMinutes"]} label="缓存分钟" onChange={onChange} />
        </div>
        <ExchangeRateCurrencySettingsPanel settings={settings} disabled={isBusy || !canManageSettings} onPatchSettings={onPatchSettings} onBlocked={onBlocked} />
      </fieldset>
    </section>
  );
}

export default ExchangeRateSettingsPanel;

export function ExchangeRateCurrencySettingsPanel({
  settings,
  disabled,
  onPatchSettings,
  onBlocked,
}: {
  settings: SettingsRecord;
  disabled: boolean;
  onPatchSettings: (patches: SettingPatch[]) => void;
  onBlocked: (message: string) => void;
}) {
  const selectedCurrencies = normalizeCurrencyList(readStringArray(settings, exchangeRateSelectedCurrenciesPath));
  const configuredSupportedCurrencies = normalizeCurrencyList(readStringArray(settings, exchangeRateAllSupportedCurrenciesPath));
  const supportedCurrencies = normalizeCurrencyList([
    ...(configuredSupportedCurrencies.length > 0 ? configuredSupportedCurrencies : defaultExchangeCurrencies),
    ...selectedCurrencies,
  ]);
  const selectedCurrencySet = new Set(selectedCurrencies);
  const availableCurrencies = supportedCurrencies.filter((currency) => !selectedCurrencySet.has(currency));
  const lastCurrencyListUpdateTime = readSettingString(settings, exchangeRateLastCurrencyListUpdateTimePath);
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

    if (configuredSupportedCurrencies.length === 0) {
      patches.push({ path: exchangeRateAllSupportedCurrenciesPath, value: supportedCurrencies });
    }

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
          候选 {availableCurrencies.length} 种，上次更新 {formatSettingDateTime(lastCurrencyListUpdateTime)}
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
          <button className="icon-button compact-icon-button" type="button" title="添加到常用货币" disabled={!canAddCurrency} onClick={addSelectedCurrency}>
            <Plus size={16} aria-hidden="true" />
          </button>
          <button className="icon-button compact-icon-button" type="button" title="移出常用货币" disabled={!canRemoveCurrency} onClick={removeSelectedCurrency}>
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
          <button className="icon-button compact-icon-button" type="button" title="上移常用货币" disabled={!canMoveCurrencyUp} onClick={() => moveSelectedCurrency(-1)}>
            <ArrowUp size={16} aria-hidden="true" />
          </button>
          <button className="icon-button compact-icon-button" type="button" title="下移常用货币" disabled={!canMoveCurrencyDown} onClick={() => moveSelectedCurrency(1)}>
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
        size={10}
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
