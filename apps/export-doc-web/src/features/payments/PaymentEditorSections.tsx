import { useState, type ReactNode } from "react";
import type { ApiPaymentDto } from "../../api/index.ts";
import { DocumentEditorNavigation, DocumentEditorTabPanel } from "../../ui/DocumentEditorTabs.tsx";
import { useDocumentEditorValidation } from "../../ui/useDocumentEditorValidation.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { formatAmount } from "../../ui/formUtils.ts";

const sections = [
  { id: "basic", label: "基本信息", description: "填写付款单号与日期，选择收款方及付款方式。" },
  { id: "business", label: "业务与备用字段", description: "填写本次付款的货物、数量和业务资料，备用字段按需展开。" },
  { id: "amounts", label: "金额与费用", description: "填写付款金额和各项费用，费用合计自动汇入人民币金额。" },
  { id: "report", label: "预览与导出", description: "选择付款模板，核对当前草稿并生成凭证。" },
] as const;
type SectionId = typeof sections[number]["id"];
const readSection = (value: string | null) => sections.find((section) => section.id === value)?.id ?? "basic";

export function PaymentEditorSections({ payment, isNew, busy, saving, editable, hasUnsavedChanges, basic, business, amounts, report }: {
  payment: ApiPaymentDto; isNew: boolean; busy: boolean; saving: boolean; editable: boolean; hasUnsavedChanges: boolean;
  basic: ReactNode; business: ReactNode; amounts: ReactNode; report: ReactNode;
}) {
  const [activeSection, setActiveSection] = useState<SectionId>("basic");
  const { validationMessage, revealInvalidField, clearValidationMessage } = useDocumentEditorValidation(activeSection, setActiveSection, readSection);
  return <div className="document-editor-sections" onInvalidCapture={revealInvalidField} onInputCapture={clearValidationMessage}>
    <DocumentEditorNavigation prefix="payment" label="付款报销" title={payment.voucherNo || payment.invoiceNo || (isNew ? "新建付款报销" : "付款报销")}
      summary={`${formatAmount(payment.usdAmount, "USD")} / ${formatAmount(payment.cnyAmount, "CNY")}`}
      tabs={sections} activeSection={activeSection} onNavigate={setActiveSection} busy={busy} saving={saving} editable={editable}
      isNew={isNew} hasUnsavedChanges={hasUnsavedChanges} />
    {validationMessage && <InlineNotice tone="error" title="请完善后保存">{validationMessage}</InlineNotice>}
    {sections.map(({ id }) => <DocumentEditorTabPanel key={id} prefix="payment" id={id} activeSection={activeSection} eager={id !== "report"}>
      {id === "report" ? report : <fieldset className="permission-fieldset" disabled={!editable || saving}>
        {{ basic, business, amounts }[id]}
      </fieldset>}
    </DocumentEditorTabPanel>)}
  </div>;
}
