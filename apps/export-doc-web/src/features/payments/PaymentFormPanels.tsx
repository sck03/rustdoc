import { useEffect, useState } from "react";
import { CreditCard, RefreshCw, Save, Settings } from "lucide-react";
import { ApiPayeeDto, ApiPaymentDto } from "../../api/index.ts";
import { DateField, EditableComboField, NumberField, SelectField, TextAreaField, TextField } from "../../ui/FormFields.tsx";
import { formatAmount } from "../../ui/formUtils.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { CustomOptionMap, getCustomOptions } from "../custom-options/customOptionModel.ts";

type PaymentPatch = Partial<ApiPaymentDto>;

export function PaymentBasicInfoPanel({
  payment,
  isBusy,
  isReferenceDataBusy,
  payees,
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
  isBusy: boolean;
  isReferenceDataBusy: boolean;
  payees: ApiPayeeDto[];
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

    const payee = payees.find((item) => item.id === payment.payeeId);
    if (!payee) {
      return;
    }

    const accountNo = normalizeAccountNo(payment.accountNo);
    if (payee.usdAccount && normalizeAccountNo(payee.usdAccount) === accountNo) {
      setAccountType("usd");
      return;
    }

    if (payee.rmbAccount && normalizeAccountNo(payee.rmbAccount) === accountNo) {
      setAccountType("rmb");
    }
  }, [payees, payment.accountNo, payment.payeeId]);

  function applyPayee(payeeIdValue: string, nextAccountType = accountType) {
    const payeeId = Number(payeeIdValue);
    if (!Number.isInteger(payeeId) || payeeId <= 0) {
      onChange({ payeeId: 0 });
      return;
    }

    const payee = payees.find((item) => item.id === payeeId);
    if (!payee) {
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
    if (payment.payeeId && payment.payeeId > 0) {
      applyPayee(String(payment.payeeId), nextAccountType);
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
          <button className="command-button" type="submit" disabled={isBusy}>
            <Save size={17} aria-hidden="true" />
            <span>保存</span>
          </button>
        </div>
      </div>
      {referenceDataMessage ? <InlineNotice tone="warning" title="参考资料未完整加载">{referenceDataMessage}</InlineNotice> : null}
      <div className="field-grid">
        <TextField label="发票号" value={payment.invoiceNo} onChange={(value) => onChange({ invoiceNo: value })} />
        <DateField label="付款日期" value={payment.paymentDate} onChange={(value) => onChange({ paymentDate: value })} />
        <DateField label="出运日期" value={payment.shipmentDate} onChange={(value) => onChange({ shipmentDate: value })} />
        <DateField label="收票日期" value={payment.receiptDate} onChange={(value) => onChange({ receiptDate: value })} />
        <SelectField
          label="支付对象资料"
          value={payment.payeeId && payment.payeeId > 0 ? String(payment.payeeId) : ""}
          disabled={isBusy || isReferenceDataBusy}
          options={payees.map((payee) => ({
            value: String(payee.id),
            label: payee.category ? `${payee.name} / ${payee.category}` : payee.name,
          }))}
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
        />
        <EditableComboField
          label="付款方式"
          value={payment.paymentMethod ?? ""}
          options={getCustomOptions(customOptions, "PaymentMethod")}
          onChange={(value) => onChange({ paymentMethod: value })}
          onCommit={(value) => onCommitCustomOption?.("PaymentMethod", value)}
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
        <h2>业务信息</h2>
      </div>
      <div className="field-grid">
        <TextField label="部门" value={payment.department ?? ""} onChange={(value) => onChange({ department: value })} />
        <TextField label="部门 ID" value={payment.departmentId ?? ""} onChange={(value) => onChange({ departmentId: value })} />
        <TextField label="项目" value={payment.project ?? ""} onChange={(value) => onChange({ project: value })} />
        <TextField label="品名" value={payment.goodsName ?? ""} onChange={(value) => onChange({ goodsName: value })} />
        <TextField label="数量" value={payment.quantity ?? ""} onChange={(value) => onChange({ quantity: value })} />
        <TextField label="出运国家" value={payment.shipmentCountry ?? ""} onChange={(value) => onChange({ shipmentCountry: value })} />
        <TextField label="公司范围" value={payment.companyScope ?? ""} onChange={(value) => onChange({ companyScope: value })} />
      </div>
      <TextAreaField
        className="field-grid-span-all"
        label="备注"
        value={payment.notes ?? ""}
        onChange={(value) => onChange({ notes: value })}
      />
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
        <NumberField label="USD 金额" value={payment.usdAmount} onChange={(value) => onChange({ usdAmount: value })} />
        <NumberField label="CNY 金额" value={payment.cnyAmount} onChange={(value) => onChange({ cnyAmount: value })} />
        <NumberField label="差旅费" value={payment.travelExpense} onChange={(value) => onChange({ travelExpense: value })} />
        <NumberField
          label="业务招待费"
          value={payment.businessEntertainmentExpense}
          onChange={(value) => onChange({ businessEntertainmentExpense: value })}
        />
        <NumberField label="电话费" value={payment.telephoneExpense} onChange={(value) => onChange({ telephoneExpense: value })} />
        <NumberField label="办公费" value={payment.officeExpense} onChange={(value) => onChange({ officeExpense: value })} />
        <NumberField label="维修费" value={payment.repairExpense} onChange={(value) => onChange({ repairExpense: value })} />
        <NumberField
          label="运杂费"
          value={payment.freightMiscExpense}
          onChange={(value) => onChange({ freightMiscExpense: value })}
        />
        <NumberField label="商检费" value={payment.inspectionExpense} onChange={(value) => onChange({ inspectionExpense: value })} />
        <NumberField label="其他费用" value={payment.otherExpense} onChange={(value) => onChange({ otherExpense: value })} />
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
