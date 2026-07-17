export type EditableInvoiceItemField =
  | "poNumber"
  | "styleNo"
  | "styleName"
  | "styleNameCN"
  | "fabricComposition"
  | "brand"
  | "hsCode"
  | "origin"
  | "quantity"
  | "unitEN"
  | "unitCN"
  | "pcsPerCtn"
  | "cartons"
  | "ctnUnitEN"
  | "ctnUnitCN"
  | "length"
  | "width"
  | "height"
  | "volume"
  | "gwPerCtn"
  | "gwTotal"
  | "nwPerCtn"
  | "nwTotal"
  | "unitPrice"
  | "totalPrice"
  | "purchasePrice"
  | "purchaseTotal"
  | "taxRebateRate"
  | "spare1"
  | "spare2"
  | "spare3";

export type InvoiceItemColumnKind = "text" | "number";

export interface InvoiceItemColumnDefinition {
  field: EditableInvoiceItemField;
  header: string;
  ariaName: string;
  kind: InvoiceItemColumnKind;
  colClassName: string;
  headerClassName?: string;
}

export const invoiceItemEditableColumns: InvoiceItemColumnDefinition[] = [
  { field: "poNumber", header: "PO", ariaName: "PO", kind: "text", colClassName: "item-short-col" },
  { field: "styleNo", header: "款号", ariaName: "款号", kind: "text", colClassName: "item-short-col" },
  { field: "styleName", header: "英文品名", ariaName: "英文品名", kind: "text", colClassName: "item-wide-col" },
  { field: "styleNameCN", header: "中文品名", ariaName: "中文品名", kind: "text", colClassName: "item-wide-col" },
  { field: "fabricComposition", header: "成分", ariaName: "成分", kind: "text", colClassName: "item-wide-col" },
  { field: "brand", header: "品牌", ariaName: "品牌", kind: "text", colClassName: "item-short-col" },
  { field: "hsCode", header: "HS 编码", ariaName: "HS 编码", kind: "text", colClassName: "item-short-col" },
  { field: "origin", header: "原产地", ariaName: "原产地", kind: "text", colClassName: "item-short-col" },
  { field: "quantity", header: "数量", ariaName: "数量", kind: "number", colClassName: "item-number-col", headerClassName: "amount-cell" },
  { field: "unitEN", header: "单位 EN", ariaName: "英文单位", kind: "text", colClassName: "item-short-col" },
  { field: "unitCN", header: "单位 CN", ariaName: "中文单位", kind: "text", colClassName: "item-short-col" },
  { field: "pcsPerCtn", header: "每箱", ariaName: "每箱数量", kind: "number", colClassName: "item-number-col", headerClassName: "amount-cell" },
  { field: "cartons", header: "箱数", ariaName: "箱数", kind: "number", colClassName: "item-number-col", headerClassName: "amount-cell" },
  { field: "ctnUnitEN", header: "箱单位", ariaName: "箱单位", kind: "text", colClassName: "item-short-col" },
  { field: "ctnUnitCN", header: "箱单位 CN", ariaName: "箱中文单位", kind: "text", colClassName: "item-short-col" },
  { field: "length", header: "长", ariaName: "长度", kind: "number", colClassName: "item-number-col", headerClassName: "amount-cell" },
  { field: "width", header: "宽", ariaName: "宽度", kind: "number", colClassName: "item-number-col", headerClassName: "amount-cell" },
  { field: "height", header: "高", ariaName: "高度", kind: "number", colClassName: "item-number-col", headerClassName: "amount-cell" },
  { field: "volume", header: "体积", ariaName: "体积", kind: "number", colClassName: "item-number-col", headerClassName: "amount-cell" },
  { field: "gwPerCtn", header: "毛重/箱", ariaName: "每箱毛重", kind: "number", colClassName: "item-number-col", headerClassName: "amount-cell" },
  { field: "gwTotal", header: "总毛重", ariaName: "总毛重", kind: "number", colClassName: "item-number-col", headerClassName: "amount-cell" },
  { field: "nwPerCtn", header: "净重/箱", ariaName: "每箱净重", kind: "number", colClassName: "item-number-col", headerClassName: "amount-cell" },
  { field: "nwTotal", header: "总净重", ariaName: "总净重", kind: "number", colClassName: "item-number-col", headerClassName: "amount-cell" },
  { field: "unitPrice", header: "单价", ariaName: "单价", kind: "number", colClassName: "item-number-col", headerClassName: "amount-cell" },
  { field: "totalPrice", header: "金额", ariaName: "金额", kind: "number", colClassName: "item-number-col", headerClassName: "amount-cell" },
  { field: "purchasePrice", header: "采购价", ariaName: "采购价", kind: "number", colClassName: "item-number-col", headerClassName: "amount-cell" },
  { field: "purchaseTotal", header: "采购额", ariaName: "采购额", kind: "number", colClassName: "item-number-col", headerClassName: "amount-cell" },
  { field: "taxRebateRate", header: "退税率", ariaName: "退税率", kind: "number", colClassName: "item-number-col", headerClassName: "amount-cell" },
  { field: "spare1", header: "备注 1", ariaName: "备注 1", kind: "text", colClassName: "item-short-col" },
  { field: "spare2", header: "备注 2", ariaName: "备注 2", kind: "text", colClassName: "item-short-col" },
  { field: "spare3", header: "备注 3", ariaName: "备注 3", kind: "text", colClassName: "item-short-col" },
];

export const firstEditableInvoiceItemField = invoiceItemEditableColumns[0].field;
