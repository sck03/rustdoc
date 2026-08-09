import type { ApiInvoiceDetailDto } from "../../api/index.ts";
import { EditableComboField, NumberField, TextField } from "../../ui/FormFields.tsx";
import { getCustomOptions, type CustomOptionMap } from "../custom-options/customOptionModel.ts";
import {
  invoiceItemVolumeDisplayValue,
  invoiceItemWeightDisplayValue,
} from "./invoiceItemsEditorModel.ts";

type InvoicePatch = Partial<ApiInvoiceDetailDto>;

export function InvoiceShippingTermsPanel({
  invoice,
  isNewInvoice = false,
  isEditable,
  onChange,
  customOptions,
  onCommitCustomOption,
}: {
  invoice: ApiInvoiceDetailDto;
  isNewInvoice?: boolean;
  isEditable: boolean;
  onChange: (next: InvoicePatch) => void;
  customOptions?: CustomOptionMap;
  onCommitCustomOption?: (optionType: string, value: string) => void;
}) {
  const totalsFields = (
    <>
      <NumberField label="总箱数" value={invoice.totalCartons ?? 0} disabled description="由商品明细自动汇总" onChange={() => undefined} />
      <NumberField label="总数量" value={invoice.totalQuantity ?? 0} disabled description="由商品明细自动汇总" onChange={() => undefined} />
      <NumberField label="总毛重" value={invoice.totalGrossWeight ?? 0} disabled step="0.01" formatValue={invoiceItemWeightDisplayValue} description="由商品明细自动汇总" onChange={() => undefined} />
      <NumberField label="总净重" value={invoice.totalNetWeight ?? 0} disabled step="0.01" formatValue={invoiceItemWeightDisplayValue} description="由商品明细自动汇总" onChange={() => undefined} />
      <NumberField label="总体积" value={invoice.totalVolume ?? 0} disabled step="0.001" formatValue={invoiceItemVolumeDisplayValue} description="由商品明细自动汇总" onChange={() => undefined} />
      <NumberField label="采购总额" value={invoice.totalPurchaseAmount ?? 0} disabled description="由商品明细自动汇总" onChange={() => undefined} />
      <NumberField label="退税总额" value={invoice.totalTaxRefundAmount ?? 0} disabled description="由商品明细自动汇总" onChange={() => undefined} />
      <NumberField label="利润总额" value={invoice.totalProfit ?? 0} disabled description="按币种和汇率自动计算" onChange={() => undefined} />
    </>
  );

  return (
    <section className="form-section information-tier-required" aria-label="运输与条款">
      <div className="section-header">
        <h2>运输与条款</h2>
      </div>
      <div className="field-grid">
        <TextField label="目的国" value={invoice.destinationCountry ?? ""} disabled={!isEditable} onChange={(value) => onChange({ destinationCountry: value })} />
        <EditableComboField
          label="装运港"
          value={invoice.portOfLoading ?? ""}
          disabled={!isEditable}
          options={getCustomOptions(customOptions, "PortOfLoading")}
          onChange={(value) => onChange({ portOfLoading: value })}
          onCommit={(value) => onCommitCustomOption?.("PortOfLoading", value)}
        />
        <EditableComboField
          label="目的港"
          value={invoice.portOfDestination ?? ""}
          disabled={!isEditable}
          options={getCustomOptions(customOptions, "PortOfDestination")}
          onChange={(value) => onChange({ portOfDestination: value })}
          onCommit={(value) => onCommitCustomOption?.("PortOfDestination", value)}
        />
        <TextField label="贸易条款" value={invoice.tradeTerms ?? ""} disabled={!isEditable} onChange={(value) => onChange({ tradeTerms: value })} />
        <EditableComboField
          label="运输方式"
          value={invoice.transportMode ?? ""}
          disabled={!isEditable}
          options={getCustomOptions(customOptions, "TransportMode")}
          onChange={(value) => onChange({ transportMode: value })}
          onCommit={(value) => onCommitCustomOption?.("TransportMode", value)}
        />
        <EditableComboField
          label="付款条款"
          value={invoice.paymentTerms ?? ""}
          disabled={!isEditable}
          options={getCustomOptions(customOptions, "PaymentTerms")}
          onChange={(value) => onChange({ paymentTerms: value })}
          onCommit={(value) => onCommitCustomOption?.("PaymentTerms", value)}
        />
        {isNewInvoice ? null : totalsFields}
      </div>
      {isNewInvoice ? (
        <details className="invoice-inline-details">
          <summary>汇总与派生金额</summary>
          <div className="field-grid invoice-inline-details-grid">{totalsFields}</div>
        </details>
      ) : null}
    </section>
  );
}
