import { ApiPaymentDto } from "../../api/index.ts";
import {
  dateInputToApiDate,
  normalizeText,
  numberValue,
  toDateInputValue,
} from "../../ui/formUtils.ts";

export function createEmptyPayment(): ApiPaymentDto {
  const today = dateInputToApiDate(new Date().toISOString().slice(0, 10));

  return {
    id: 0,
    invoiceNo: "",
    shipmentDate: today,
    payeeName: "",
    payerName: "",
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
    paymentDate: normalizeRequiredDate(payment.paymentDate),
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

function normalizeRequiredDate(value?: string) {
  return dateInputToApiDate(toDateInputValue(value) || new Date().toISOString().slice(0, 10));
}

function normalizeOptionalDate(value?: string) {
  const inputValue = toDateInputValue(value);
  return inputValue ? dateInputToApiDate(inputValue) : undefined;
}
