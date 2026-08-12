import { ApiPaymentDto } from "../../api/index.ts";
import {
  currentLocalDateInputValue,
  dateInputToApiDate,
  normalizeText,
  numberValue,
  toDateInputValue,
} from "../../ui/formUtils.ts";

export function createEmptyPayment(): ApiPaymentDto {
  const today = dateInputToApiDate(currentLocalDateInputValue());

  return {
    id: 0,
    ownerUserId: null,
    departmentId: "",
    companyScope: "",
    invoiceNo: "",
    shipmentDate: today,
    payeeId: 0,
    department: "",
    project: "",
    payeeName: "",
    payerName: "",
    bankName: "",
    accountNo: "",
    paymentMethod: "",
    notes: "",
    goodsName: "",
    quantity: "",
    shipmentCountry: "",
    paymentDate: today,
    receiptDate: today,
    usdAmount: 0,
    cnyAmount: 0,
    travelExpense: 0,
    businessEntertainmentExpense: 0,
    telephoneExpense: 0,
    officeExpense: 0,
    repairExpense: 0,
    freightMiscExpense: 0,
    inspectionExpense: 0,
    otherExpense: 0,
    rowVersion: "",
  };
}

export function normalizePaymentForSave(payment: ApiPaymentDto, id: number): ApiPaymentDto {
  return {
    ...payment,
    id,
    accountNo: normalizeText(payment.accountNo),
    bankName: normalizeText(payment.bankName),
    businessEntertainmentExpense: numberValue(payment.businessEntertainmentExpense),
    cnyAmount: numberValue(payment.cnyAmount),
    companyScope: normalizeText(payment.companyScope),
    department: normalizeText(payment.department),
    departmentId: normalizeText(payment.departmentId),
    freightMiscExpense: numberValue(payment.freightMiscExpense),
    goodsName: normalizeText(payment.goodsName),
    inspectionExpense: numberValue(payment.inspectionExpense),
    invoiceNo: normalizeText(payment.invoiceNo),
    notes: normalizeText(payment.notes),
    officeExpense: numberValue(payment.officeExpense),
    otherExpense: numberValue(payment.otherExpense),
    payeeName: normalizeText(payment.payeeName),
    payerName: normalizeText(payment.payerName),
    paymentDate: normalizeOptionalDate(payment.paymentDate),
    paymentMethod: normalizeText(payment.paymentMethod),
    project: normalizeText(payment.project),
    quantity: normalizeText(payment.quantity),
    receiptDate: normalizeOptionalDate(payment.receiptDate),
    repairExpense: numberValue(payment.repairExpense),
    shipmentCountry: normalizeText(payment.shipmentCountry),
    shipmentDate: normalizeOptionalDate(payment.shipmentDate),
    telephoneExpense: numberValue(payment.telephoneExpense),
    travelExpense: numberValue(payment.travelExpense),
    usdAmount: numberValue(payment.usdAmount),
  };
}

function normalizeOptionalDate(value?: string | null) {
  const inputValue = toDateInputValue(value);
  return inputValue ? dateInputToApiDate(inputValue) : null;
}

export function validatePaymentDraft(payment: ApiPaymentDto) {
  if ((payment.payeeId ?? 0) < 0) return "支付对象资料编号不能小于 0。";

  const textLimits: Array<[string | undefined, number, string]> = [
    [payment.invoiceNo, 100, "发票号"],
    [payment.department, 100, "部门"],
    [payment.paymentMethod, 100, "付款方式"],
    [payment.quantity, 100, "数量"],
    [payment.shipmentCountry, 100, "出运国家"],
    [payment.project, 200, "项目"],
    [payment.payeeName, 200, "收款方"],
    [payment.payerName, 200, "付款方"],
    [payment.bankName, 200, "银行"],
    [payment.accountNo, 100, "账号"],
    [payment.goodsName, 500, "品名"],
    [payment.notes, 2000, "备注"],
  ];
  const overlong = textLimits.find(([value, maximumLength]) => (value?.trim().length ?? 0) > maximumLength);
  if (overlong) return `${overlong[2]}不能超过 ${overlong[1]} 个字符。`;

  const amounts: Array<[number | undefined, string]> = [
    [payment.usdAmount, "USD 金额"],
    [payment.cnyAmount, "CNY 金额"],
    [payment.travelExpense, "差旅费"],
    [payment.businessEntertainmentExpense, "业务招待费"],
    [payment.telephoneExpense, "电话费"],
    [payment.officeExpense, "办公费"],
    [payment.repairExpense, "维修费"],
    [payment.freightMiscExpense, "运杂费"],
    [payment.inspectionExpense, "商检费"],
    [payment.otherExpense, "其他费用"],
  ];
  const negative = amounts.find(([value]) => Number(value ?? 0) < 0);
  if (negative) return `${negative[1]}不能小于 0。`;
  return null;
}
