import { useEffect, useState } from "react";
import { CreditCard, RefreshCw, Settings } from "lucide-react";
import { ApiPayeeDto, ApiPaymentDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { DateField, EditableComboField, NumberField, SelectField, TextAreaField, TextField } from "../../ui/FormFields.tsx";
import { formatAmount } from "../../ui/formUtils.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { RemoteSelectField } from "../../ui/RemoteSelectField.tsx";
import { CustomOptionMap, getCustomOptions } from "../custom-options/customOptionModel.ts";
import { DocumentSpareFieldsPanel } from "../../ui/DocumentSpareFieldsPanel.tsx";
import { CustomOptionSelectField } from "../custom-options/CustomOptionSelectField.tsx";
import { paymentAmountFields } from "./paymentModel.ts";

type PaymentPatch = Partial<ApiPaymentDto>;

export function PaymentBasicInfoPanel({
  payment,
  client,
  isBusy,
  isReferenceDataBusy,
  selectedPayee,
  payerNameOptions,
  referenceDataMessage,
  customOptions,
  onChange,
  onCommitCustomOption,
  onOpenPayeeManagement,
  canOpenPayeeManagement,
  onRefreshReferenceData,
}: {
  payment: ApiPaymentDto;
  client: ExportDocManagerApiClient;
  isBusy: boolean;
  isReferenceDataBusy: boolean;
  selectedPayee?: ApiPayeeDto | null;
  payerNameOptions: string[];
  referenceDataMessage: string | null;
  customOptions?: CustomOptionMap;
  onChange: (next: PaymentPatch) => void;
  onCommitCustomOption?: (optionType: string, value: string) => void;
  onOpenPayeeManagement: () => void;
  canOpenPayeeManagement: boolean;
  onRefreshReferenceData: () => void;
}) {
  const [accountType, setAccountType] = useState<"rmb" | "usd">("rmb");

  useEffect(() => {
    if (!payment.payeeId || payment.payeeId <= 0 || !payment.accountNo?.trim()) {
      return;
    }

    if (!selectedPayee || selectedPayee.id !== payment.payeeId) {
      return;
    }

    const accountNo = normalizeAccountNo(payment.accountNo);
    if (selectedPayee.usdAccount && normalizeAccountNo(selectedPayee.usdAccount) === accountNo) {
      setAccountType("usd");
      return;
    }

    if (selectedPayee.rmbAccount && normalizeAccountNo(selectedPayee.rmbAccount) === accountNo) {
      setAccountType("rmb");
    }
  }, [selectedPayee, payment.accountNo, payment.payeeId]);

  function applyPayee(payee: ApiPayeeDto | null, nextAccountType = accountType) {
    if (!payee) {
      onChange({ payeeId: 0 });
      return;
    }

    onChange({
      payeeId: payee.id,
      payeeName: payee.name ?? "",
      bankName: payee.bankName ?? "",
      accountNo: readPayeeAccount(payee, nextAccountType),
    });
  }

  function changeAccountType(value: string) {
    const nextAccountType = value === "usd" ? "usd" : "rmb";
    setAccountType(nextAccountType);
    if (selectedPayee && payment.payeeId && payment.payeeId > 0) {
      applyPayee(selectedPayee, nextAccountType);
    }
  }

  return (
    <section className="form-section" aria-label="基础信息">
      <div className="section-header">
        <h2>基础信息</h2>
        <div className="toolbar-actions">
          <button
            className="icon-button"
            type="button"
            title="刷新收付款基础资料" aria-label="刷新收付款基础资料"
            disabled={isBusy || isReferenceDataBusy}
            onClick={onRefreshReferenceData}
          >
            <RefreshCw size={17} aria-hidden="true" />
          </button>
          {canOpenPayeeManagement ? (
            <button
              className="command-button secondary"
              type="button"
              disabled={isBusy}
              onClick={onOpenPayeeManagement}
            >
              <Settings size={17} aria-hidden="true" />
              <span>收款方资料</span>
            </button>
          ) : null}
        </div>
      </div>
      {referenceDataMessage ? <InlineNotice tone="warning" title="参考资料未完整加载">{referenceDataMessage}</InlineNotice> : null}
      <div className="field-grid">
        <TextField label="付款单号" value={payment.voucherNo} onChange={(value) => onChange({ voucherNo: value })} />
        <TextField label="发票号／业务参考号" value={payment.invoiceNo} onChange={(value) => onChange({ invoiceNo: value })} />
        <DateField label="付款日期" value={payment.paymentDate} onChange={(value) => onChange({ paymentDate: value })} />
        <DateField label="收汇日期" value={payment.receiptDate} onChange={(value) => onChange({ receiptDate: value })} />
        <RemoteSelectField<ApiPayeeDto>
          label="支付对象资料"
          value={payment.payeeId && payment.payeeId > 0 ? String(payment.payeeId) : ""}
          selectedOption={selectedPayee}
          selectedLabel={payment.payeeName || undefined}
          disabled={isBusy}
          queryKey={["master-data", "payees", "lookup"]}
          loadOptions={async (keyword, signal) => (await client.listPayeesPage({
            keyword: keyword || undefined,
            pageNumber: 1,
            pageSize: 50,
          }, { signal })).items}
          getValue={(payee) => String(payee.id)}
          getLabel={(payee) => payee.category ? `${payee.name} / ${payee.category}` : payee.name}
          onChange={applyPayee}
        />
        <SelectField
          label="账号类型"
          value={accountType}
          disabled={isBusy}
          includeEmptyOption={false}
          options={[
            { value: "rmb", label: "人民币账号" },
            { value: "usd", label: "美金账号" },
          ]}
          onChange={changeAccountType}
        />
        <TextField className="field-grid-span-2" label="收款方" value={payment.payeeName} onChange={(value) => onChange({ payeeName: value })} />
        <EditableComboField
          className="field-grid-span-2"
          label="付款方"
          value={payment.payerName}
          options={payerNameOptions}
          onChange={(value) => onChange({ payerName: value })}
          onCommit={(value) => onCommitCustomOption?.("PaymentPayerName", value)}
        />
        <CustomOptionSelectField
          className="field-grid-span-2"
          client={client}
          optionType="PaymentMethod"
          label="付款方式"
          value={payment.paymentMethod ?? ""}
          options={getCustomOptions(customOptions, "PaymentMethod")}
          disabled={isBusy}
          onChange={(value) => onChange({ paymentMethod: value })}
        />
        <TextField className="field-grid-span-2" label="银行" value={payment.bankName ?? ""} onChange={(value) => onChange({ bankName: value })} />
        <TextField className="field-grid-span-2" label="账号" value={payment.accountNo ?? ""} onChange={(value) => onChange({ accountNo: value })} />
      </div>
    </section>
  );
}

export function PaymentBusinessInfoPanel({
  payment,
  onChange,
}: {
  payment: ApiPaymentDto;
  onChange: (next: PaymentPatch) => void;
}) {
  return (
    <section className="form-section" aria-label="业务信息">
      <div className="section-header">
        <h2>付款业务信息</h2>
      </div>
      <p className="form-field-description">按本次付款填写品名与合计数量，可合并同类货物；这里的内容独立保存，不会改动报关单据及其商品明细。</p>
      <div className="field-grid">
        <TextField label="部门" value={payment.department ?? ""} onChange={(value) => onChange({ department: value })} />
        <TextField label="项目" value={payment.project ?? ""} onChange={(value) => onChange({ project: value })} />
        <EditableComboField label="贸易方式" value={payment.tradeMethod} options={["L/C", "T/T", "D/P", "远期"]} onChange={(value) => onChange({ tradeMethod: value })} />
        <EditableComboField label="退税率" value={payment.taxRebateRate} options={["13%", "10%", "9%", "6%", "0%"]} onChange={(value) => onChange({ taxRebateRate: value })} />
        <TextAreaField className="field-grid-span-2" label="货物品名" value={payment.goodsName ?? ""} onChange={(value) => onChange({ goodsName: value })} />
        <TextField label="数量" value={payment.quantity ?? ""} onChange={(value) => onChange({ quantity: value })} />
        <TextField label="数量单位" value={payment.quantityUnit} onChange={(value) => onChange({ quantityUnit: value })} />
        <TextField label="出运国家" value={payment.shipmentCountry ?? ""} onChange={(value) => onChange({ shipmentCountry: value })} />
        <DateField label="出运日期" value={payment.shipmentDate} onChange={(value) => onChange({ shipmentDate: value })} />
        <TextAreaField className="field-grid-span-2" label="备注" value={payment.notes ?? ""} onChange={(value) => onChange({ notes: value })} />
      </div>
      <DocumentSpareFieldsPanel group="payment" label="付款备用字段" value={payment} onChange={onChange} />
    </section>
  );
}

export function PaymentAmountsPanel({
  payment,
  onChange,
}: {
  payment: ApiPaymentDto;
  onChange: (next: PaymentPatch) => void;
}) {
  return (
    <section className="form-section" aria-label="金额和费用">
      <div className="section-header">
        <h2>金额和费用</h2>
        <div className="editor-title payment-total">
          <CreditCard size={17} aria-hidden="true" />
          <span>{formatAmount(payment.usdAmount, "USD")} / {formatAmount(payment.cnyAmount, "CNY")}</span>
        </div>
      </div>
      <div className="field-grid">
        {paymentAmountFields.map(({ field, label }) => <NumberField key={field} label={label} value={payment[field]} onChange={(value) => onChange({ [field]: value })} />)}
      </div>
    </section>
  );
}

function readPayeeAccount(payee: ApiPayeeDto, accountType: "rmb" | "usd") {
  return accountType === "rmb" ? payee.rmbAccount ?? "" : payee.usdAccount ?? "";
}

function normalizeAccountNo(value: string) {
  return value.trim().replace(/\s+/g, "").toLowerCase();
}
