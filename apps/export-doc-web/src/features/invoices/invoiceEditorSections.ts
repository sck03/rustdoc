export const invoiceEditorSections = [
  { id: "header", label: "基本信息", description: "填写发票号，选择客户和出口商；补充资料按需展开。" },
  { id: "items", label: "商品明细", description: "录入商品与价格，金额自动汇总；较多明细可打开明细工作台。" },
  { id: "shipping", label: "运输与报关", description: "填写运输、付款条款及报关需要的补充信息。" },
  { id: "analysis", label: "利润与信用证", description: "需要时进行利润测算或核对信用证，不影响先保存草稿。" },
  { id: "report", label: "预览与导出", description: "核对单据效果，选择模板并输出文件。" },
] as const;

export type InvoiceEditorSectionId = typeof invoiceEditorSections[number]["id"];

export function readInvoiceEditorSection(value: string | null): InvoiceEditorSectionId {
  return invoiceEditorSections.find((section) => section.id === value)?.id ?? "header";
}
