import { useEffect, useState } from "react";
import { RefreshCw, RotateCcw, Save, Trash2 } from "lucide-react";
import { NumberField, SelectField } from "../../ui/FormFields.tsx";
import { buildExcelSchemeOptions, createDefaultExcelImportSettings, normalizeExcelImportSettings, readExcelImportRecordNumber, readExcelImportSchemesForSettings, readExcelImportSettingsForSettings } from "./excelImportSettingsModel.ts";
import { cloneSettings, readRecordString } from "./settingsValueUtils.ts";
import type { SettingsRecord } from "./settingsTypes.ts";

type ExcelImportFieldDefinition = {
  key: string;
  label: string;
  kind: "text" | "number";
};

const excelExporterMappingFields: ExcelImportFieldDefinition[] = [
  { key: "exporterNameCNCell", label: "出口商中文", kind: "text" },
  { key: "exporterNameCell", label: "出口商英文", kind: "text" },
  { key: "exporterAddressStartCell", label: "出口商地址起始", kind: "text" },
  { key: "exporterAddressLineCount", label: "出口商地址行数", kind: "number" },
  { key: "creditCodeCell", label: "统一信用代码", kind: "text" },
];

const excelCustomerMappingFields: ExcelImportFieldDefinition[] = [
  { key: "customerNameCell", label: "客户名称", kind: "text" },
  { key: "customerAddressStartCell", label: "客户地址起始", kind: "text" },
  { key: "customerAddressLineCount", label: "客户地址行数", kind: "number" },
  { key: "notifyPartyNameCell", label: "通知方名称", kind: "text" },
  { key: "notifyPartyAddressStartCell", label: "通知方地址起始", kind: "text" },
  { key: "notifyPartyAddressLineCount", label: "通知方地址行数", kind: "number" },
];

const excelInvoiceMappingFields: ExcelImportFieldDefinition[] = [
  { key: "invoiceDateCell", label: "发票日期", kind: "text" },
  { key: "contractNoCell", label: "合同号", kind: "text" },
  { key: "issuingBankCell", label: "开证行", kind: "text" },
  { key: "currencyCell", label: "币种", kind: "text" },
  { key: "invoiceNoCell", label: "发票号", kind: "text" },
  { key: "supervisionModeCell", label: "监管方式", kind: "text" },
  { key: "letterOfCreditNoCell", label: "信用证号", kind: "text" },
  { key: "paymentTermsCell", label: "付款方式", kind: "text" },
  { key: "transportModeCell", label: "运输方式", kind: "text" },
  { key: "tradeTermsCell", label: "贸易条款", kind: "text" },
  { key: "portOfLoadingCell", label: "起运港", kind: "text" },
  { key: "portOfDestinationCell", label: "目的港", kind: "text" },
  { key: "destinationCountryCell", label: "目的国", kind: "text" },
  { key: "shippingMarksCell", label: "唛头", kind: "text" },
];

const excelItemMappingFields: ExcelImportFieldDefinition[] = [
  { key: "itemsStartRow", label: "明细起始行", kind: "number" },
  { key: "itemsEndRow", label: "明细结束行", kind: "number" },
  { key: "poNumberCol", label: "订单号列", kind: "number" },
  { key: "styleNoCol", label: "款号列", kind: "number" },
  { key: "styleNameCol", label: "品名英文列", kind: "number" },
  { key: "fabricCompositionCol", label: "面料成分列", kind: "number" },
  { key: "styleNameCNCol", label: "品名中文列", kind: "number" },
  { key: "brandCol", label: "品牌列", kind: "number" },
  { key: "hsCodeCol", label: "HS 编码列", kind: "number" },
  { key: "originCol", label: "原产地列", kind: "number" },
  { key: "quantityCol", label: "数量列", kind: "number" },
  { key: "unitENCol", label: "单位英文列", kind: "number" },
  { key: "unitCNCol", label: "单位中文列", kind: "number" },
  { key: "cartonsCol", label: "箱数列", kind: "number" },
  { key: "ctnUnitENCol", label: "箱数单位列", kind: "number" },
  { key: "lengthCol", label: "长度列", kind: "number" },
  { key: "widthCol", label: "宽度列", kind: "number" },
  { key: "heightCol", label: "高度列", kind: "number" },
  { key: "volumeCol", label: "体积列", kind: "number" },
  { key: "gwPerCtnCol", label: "每箱毛重列", kind: "number" },
  { key: "gwTotalCol", label: "总毛重列", kind: "number" },
  { key: "nwPerCtnCol", label: "每箱净重列", kind: "number" },
  { key: "nwTotalCol", label: "总净重列", kind: "number" },
  { key: "unitPriceCol", label: "单价列", kind: "number" },
  { key: "totalPriceCol", label: "总价列", kind: "number" },
];

export function ExcelImportSettingsPanel({
  settings,
  canManageSettings,
  isBusy,
  onChange,
}: {
  settings: SettingsRecord;
  canManageSettings: boolean;
  isBusy: boolean;
  onChange: (path: string[], value: unknown) => void;
}) {
  const disabled = !canManageSettings || isBusy;
  const currentSettings = readExcelImportSettingsForSettings(settings);
  const schemes = readExcelImportSchemesForSettings(settings);
  const schemeOptions = buildExcelSchemeOptions(schemes);
  const currentSchemeName = readRecordString(currentSettings, "schemeName") || "Default";
  const [selectedSchemeName, setSelectedSchemeName] = useState("");

  useEffect(() => {
    if (schemes.length === 0) {
      setSelectedSchemeName("");
      return;
    }

    const currentStillExists = schemes.some((scheme) => readRecordString(scheme, "schemeName") === selectedSchemeName);
    if (!selectedSchemeName || !currentStillExists) {
      const matchingCurrent = schemes.find((scheme) => readRecordString(scheme, "schemeName") === currentSchemeName);
      setSelectedSchemeName(readRecordString(matchingCurrent ?? schemes[0], "schemeName"));
    }
  }, [currentSchemeName, schemes, selectedSchemeName]);

  function updateCurrentField(key: string, value: unknown) {
    onChange(["excelImport", key], value);
  }

  function loadSelectedScheme() {
    const scheme = schemes.find((item) => readRecordString(item, "schemeName") === selectedSchemeName);
    if (!scheme) {
      return;
    }

    onChange(["excelImport"], cloneSettings(scheme));
  }

  function saveCurrentScheme() {
    const normalizedName = currentSchemeName.trim();
    if (!normalizedName) {
      return;
    }

    const scheme = {
      ...normalizeExcelImportSettings(currentSettings),
      schemeName: normalizedName,
    };
    const nextSchemes = [
      ...schemes.filter((item) => readRecordString(item, "schemeName") !== normalizedName),
      scheme,
    ];
    onChange(["excelImport"], scheme);
    onChange(["excelImportSchemes"], nextSchemes);
    setSelectedSchemeName(normalizedName);
  }

  function deleteSelectedScheme() {
    if (!selectedSchemeName) {
      return;
    }

    onChange(
      ["excelImportSchemes"],
      schemes.filter((item) => readRecordString(item, "schemeName") !== selectedSchemeName),
    );
  }

  function restoreDefaultMapping() {
    onChange(["excelImport"], createDefaultExcelImportSettings(currentSchemeName));
  }

  return (
    <section className="form-section excel-import-settings-section" aria-label="Excel 导入方案">
      <div className="section-header">
        <h2>Excel 导入方案</h2>
        <div className="toolbar-actions">
          <button className="command-button secondary" type="button" disabled={disabled || !selectedSchemeName} onClick={loadSelectedScheme}>
            <RefreshCw size={17} aria-hidden="true" />
            <span>加载方案</span>
          </button>
          <button className="command-button secondary" type="button" disabled={disabled || !currentSchemeName.trim()} onClick={saveCurrentScheme}>
            <Save size={17} aria-hidden="true" />
            <span>保存方案</span>
          </button>
          <button className="icon-button" type="button" title="恢复默认映射" aria-label="恢复默认映射" disabled={disabled} onClick={restoreDefaultMapping}>
            <RotateCcw size={18} aria-hidden="true" />
          </button>
          <button className="icon-button" type="button" title="删除方案" aria-label="删除方案" disabled={disabled || !selectedSchemeName} onClick={deleteSelectedScheme}>
            <Trash2 size={18} aria-hidden="true" />
          </button>
        </div>
      </div>
      <fieldset className="settings-fieldset" disabled={!canManageSettings}>
        <div className="field-grid">
          <label>
            <span>当前方案名</span>
            <input
              value={currentSchemeName}
              disabled={disabled}
              onChange={(event) => updateCurrentField("schemeName", event.target.value)}
            />
          </label>
          <SelectField
            label="已有方案"
            value={selectedSchemeName}
            disabled={disabled || schemeOptions.length === 0}
            options={schemeOptions.length > 0 ? schemeOptions : [{ value: "", label: "暂无方案" }]}
            onChange={setSelectedSchemeName}
          />
        </div>
        <div className="batch-export-items-toolbar">
          <span>{schemes.length} 个已保存方案</span>
          <span>{currentSchemeName || "未命名"}</span>
        </div>
        <ExcelImportFieldGroup title="出口商" fields={excelExporterMappingFields} settings={currentSettings} disabled={disabled} onChange={updateCurrentField} />
        <ExcelImportFieldGroup title="客户与通知方" fields={excelCustomerMappingFields} settings={currentSettings} disabled={disabled} onChange={updateCurrentField} />
        <ExcelImportFieldGroup title="发票与运输" fields={excelInvoiceMappingFields} settings={currentSettings} disabled={disabled} onChange={updateCurrentField} />
        <ExcelImportFieldGroup title="明细行与列" fields={excelItemMappingFields} settings={currentSettings} disabled={disabled} onChange={updateCurrentField} />
      </fieldset>
    </section>
  );
}

export default ExcelImportSettingsPanel;

function ExcelImportFieldGroup({
  title,
  fields,
  settings,
  disabled,
  onChange,
}: {
  title: string;
  fields: ExcelImportFieldDefinition[];
  settings: SettingsRecord;
  disabled: boolean;
  onChange: (key: string, value: unknown) => void;
}) {
  return (
    <div className="excel-import-field-group">
      <h3>{title}</h3>
      <div className="field-grid">
        {fields.map((field) =>
          field.kind === "number" ? (
            <NumberField
              key={field.key}
              label={field.label}
              value={readExcelImportRecordNumber(settings, field.key)}
              disabled={disabled}
              step="1"
              onChange={(value) => onChange(field.key, value)}
            />
          ) : (
            <label key={field.key}>
              <span>{field.label}</span>
              <input
                value={readRecordString(settings, field.key)}
                disabled={disabled}
                onChange={(event) => onChange(field.key, event.target.value)}
              />
            </label>
          ),
        )}
      </div>
    </div>
  );
}
