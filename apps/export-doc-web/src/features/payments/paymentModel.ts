import { ApiPaymentDto, type ApiPaymentGoodsItem } from "../../api/index.ts";
import { documentSpareKeys, mapDocumentSpareFields } from "../../ui/documentSpareFields.ts";
import { sumDecimalStrings } from "../../ui/decimalSum.ts";
import {
  dateInputToApiDate,
  normalizeText,
  toDateInputValue,
} from "../../ui/formUtils.ts";

export const paymentExpenseFields = [
  { field: "travelExpense", label: "差旅费" },
  { field: "businessEntertainmentExpense", label: "业务招待费" },
  { field: "telephoneExpense", label: "电话费" },
  { field: "officeExpense", label: "办公费" },
  { field: "repairExpense", label: "维修费" },
  { field: "freightMiscExpense", label: "运杂费" },
  { field: "inspectionExpense", label: "商检费" },
  { field: "otherExpense", label: "其他费用" },
] as const;
export const paymentAmountFields = [{ field: "usdAmount", label: "USD 金额" }, { field: "cnyAmount", label: "CNY 金额" }, ...paymentExpenseFields] as const;

export function paymentGoodsItems(payment: ApiPaymentDto): ApiPaymentGoodsItem[] {
  if (payment.goodsItems?.length) return payment.goodsItems;
  const item = { goodsName: payment.goodsName, quantity: payment.quantity, quantityUnit: payment.quantityUnit };
  return Object.values(item).some(value => value?.trim()) ? [item] : [];
}

export function paymentGoodsPatch(goodsItems: ApiPaymentGoodsItem[]): Partial<ApiPaymentDto> {
  return { goodsItems, goodsName: "", quantity: "", quantityUnit: "" };
}

const paymentTextFields = ["accountNo", "bankName", "companyScope", "department", "departmentId", "goodsName", "invoiceNo", "voucherNo", "tradeMethod", "taxRebateRate", "quantityUnit", "notes", "payeeName", "payerName", "paymentMethod", "project", "quantity", "shipmentCountry"] as const;
function fieldValues<K extends string>(keys: readonly K[], get: (key: K) => string) {
  return Object.fromEntries(keys.map(key => [key,get(key)])) as Record<K,string>;
}

function validPaymentAmount(value: string) {
  return value.length <= 60 && /^(?:0|[1-9]\d*)(?:\.\d{1,28})?$/.test(value)
    && BigInt(value.replace(".", "")) <= 79228162514264337593543950335n;
}

export function calculatePaymentExpenseTotal(payment: ApiPaymentDto) {
  const amounts = paymentExpenseFields.map(({ field }) => payment[field]?.trim() || "0");
  return amounts.every(validPaymentAmount) ? sumDecimalStrings(amounts) : "";
}

export function createEmptyPayment(businessDate: string): ApiPaymentDto {
  const today = dateInputToApiDate(businessDate);
  if (!today) throw new Error("服务端业务日期无效。");

  return {
    id: 0,
    ...fieldValues(paymentTextFields, () => ""),
    ...fieldValues(paymentAmountFields.map(({field})=>field), () => "0"),
    ownerUserId: null,
    ...mapDocumentSpareFields({}),
    shipmentDate: today,
    payeeId: 0,
    paymentDate: today,
    receiptDate: today,
    rowVersion: "",
    goodsItems: [],
  };
}

export function normalizePaymentForSave(payment: ApiPaymentDto, id: number): ApiPaymentDto {
  return {
    ...payment,
    ...fieldValues(paymentTextFields, key => normalizeText(payment[key])),
    ...paymentGoodsPatch(paymentGoodsItems(payment).map(item => ({ goodsName: normalizeText(item.goodsName),
      quantity: normalizeText(item.quantity), quantityUnit: normalizeText(item.quantityUnit) }))
      .filter(item => Object.values(item).some(Boolean))),
    ...Object.fromEntries(paymentAmountFields.map(({field}) => [field, payment[field].trim() || "0"])),
    id,
    ...mapDocumentSpareFields(payment, normalizeText),
    paymentDate: normalizeOptionalDate(payment.paymentDate),
    receiptDate: normalizeOptionalDate(payment.receiptDate),
    shipmentDate: normalizeOptionalDate(payment.shipmentDate),
  };
}

function normalizeOptionalDate(value?: string | null) {
  const inputValue = toDateInputValue(value);
  return inputValue ? dateInputToApiDate(inputValue) : null;
}

export function validatePaymentDraft(payment: ApiPaymentDto) {
  if ((payment.payeeId ?? 0) < 0) return "支付对象资料编号不能小于 0。";
  const goods = paymentGoodsItems(payment);
  if (goods.length > 4) return "货物明细最多填写 4 项。";
  for (const [index, item] of goods.entries()) {
    for (const [field, limit, label] of [["goodsName", 500, "货物品名"], ["quantity", 100, "数量"], ["quantityUnit", 20, "数量单位"]] as const) {
      if (item[field].trim().length > limit) return `第 ${index + 1} 项${label}不能超过 ${limit} 个字符。`;
    }
  }

  const textLimits: Array<[string | undefined, number, string]> = [
    [payment.invoiceNo, 100, "发票号"],
    [payment.voucherNo, 100, "付款单号"],
    [payment.tradeMethod, 100, "贸易方式"],
    [payment.taxRebateRate, 100, "退税率"],
    [payment.quantityUnit, 20, "数量单位"],
    ...documentSpareKeys.map((key, index): [string, number, string] => [payment[key], 500, `备用字段${index + 1}`]),
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

  const invalid = paymentAmountFields.find(({ field }) => !validPaymentAmount(payment[field].trim() || "0"));
  return invalid ? `${invalid.label}必须是范围内的非负十进制金额。` : null;
}
