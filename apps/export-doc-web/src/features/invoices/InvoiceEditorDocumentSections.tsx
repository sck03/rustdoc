import type { ReactNode } from "react";
import { useDocumentEditorValidation } from "../../ui/useDocumentEditorValidation.ts";
import type {
  ApiCustomerDto,
  ApiExporterDto,
  ApiInvoiceDetailDto,
  ApiInvoiceStatusHistoryDto,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import type { CustomOptionMap } from "../custom-options/customOptionModel.ts";
import type { ExporterSealType } from "../master-data/ExporterSealField.tsx";
import { InvoiceBasicInfoPanel } from "./InvoiceBasicInfoPanel.tsx";
import { InvoiceExtendedFieldsPanel } from "./InvoiceExtendedFieldsPanel.tsx";
import { InvoicePartiesPanel } from "./InvoicePartiesPanel.tsx";
import { InvoiceShippingTermsPanel } from "./InvoiceShippingTermsPanel.tsx";
import { InvoiceEditorNavigation } from "./InvoiceEditorNavigation.tsx";
import { InvoiceLetterOfCreditPanel } from "./InvoiceLetterOfCreditPanel.tsx";
import { InvoiceProfitAnalysisPanel } from "./InvoiceProfitAnalysisPanel.tsx";
import { InvoiceReportPreviewPanel } from "./InvoiceReportPreviewPanel.tsx";
import { InvoiceEditorSection } from "./InvoiceEditorSection.tsx";
import { readInvoiceEditorSection, type InvoiceEditorSectionId } from "./invoiceEditorSections.ts";
import { isMeaningfulInvoiceItem } from "./invoiceItemsEditorModel.ts";
import { formatAmount } from "../../ui/formUtils.ts";
import { InlineNotice } from "../../ui/PageState.tsx";

type InvoiceEditorDocumentSectionsProps = {
  client: ExportDocManagerApiClient;
  invoice: ApiInvoiceDetailDto;
  invoiceId: number;
  activeSection: InvoiceEditorSectionId;
  reportInvoiceId: number;
  invoiceDraft?: ApiInvoiceDetailDto;
  selectedCustomer?: ApiCustomerDto;
  selectedExporter?: ApiExporterDto;
  selectedCustomerEmail: string;
  customOptions: CustomOptionMap;
  statusHistory?: ApiInvoiceStatusHistoryDto[];
  statusHistoryLoading: boolean;
  statusHistoryMessage: string | null;
  itemsPanel: ReactNode;
  cloneInvoiceTypeLabel: string;
  isEditable: boolean;
  isBusy: boolean;
  isSaving: boolean;
  hasUnsavedChanges: boolean;
  canOpenSingleWindowDocuments: boolean;
  canCloneInvoiceType: boolean;
  canUnverifyInvoice: boolean;
  canTransitionStatus: boolean;
  canCancelStatus: boolean;
  canUseAdvancedTools: boolean;
  canManageExporterSeals: boolean;
  cloneInvoiceTypeBusy: boolean;
  unverifyInvoiceBusy: boolean;
  transitionStatusBusy: boolean;
  partyBusy: boolean;
  partyMessage: string | null;
  sealBusy: boolean;
  profitAnalysisDisabled: boolean;
  letterOfCreditDisabled: boolean;
  letterOfCreditReviewDisabled: boolean;
  onNavigate: (sectionId: InvoiceEditorSectionId) => void;
  onUppercase: () => void;
  onChange: (next: Partial<ApiInvoiceDetailDto>) => void;
  onTransitionStatus: () => void;
  onCancelStatus: () => void;
  onCloneInvoiceType: () => void;
  onUnverifyInvoice: () => void;
  onOpenCustomsCoo: () => void;
  onOpenAgentConsignment: () => void;
  onCommitCustomOption: (optionType: string, value: string) => void;
  onRefreshParties: () => void;
  onSealUpload: (sealType: ExporterSealType, file: File) => void;
  onSealError: (error: unknown) => void;
  onClearPageMessages: () => void;
  onLetterOfCreditBusyChange: (busy: boolean) => void;
};

export function InvoiceEditorDocumentSections({
  client,
  invoice,
  invoiceId,
  activeSection,
  reportInvoiceId,
  invoiceDraft,
  selectedCustomer,
  selectedExporter,
  selectedCustomerEmail,
  customOptions,
  statusHistory,
  statusHistoryLoading,
  statusHistoryMessage,
  itemsPanel,
  cloneInvoiceTypeLabel,
  isEditable,
  isBusy,
  isSaving,
  hasUnsavedChanges,
  canOpenSingleWindowDocuments,
  canCloneInvoiceType,
  canUnverifyInvoice,
  canTransitionStatus,
  canCancelStatus,
  canUseAdvancedTools,
  canManageExporterSeals,
  cloneInvoiceTypeBusy,
  unverifyInvoiceBusy,
  transitionStatusBusy,
  partyBusy,
  partyMessage,
  sealBusy,
  profitAnalysisDisabled,
  letterOfCreditDisabled,
  letterOfCreditReviewDisabled,
  onNavigate,
  onUppercase,
  onChange,
  onTransitionStatus,
  onCancelStatus,
  onCloneInvoiceType,
  onUnverifyInvoice,
  onOpenCustomsCoo,
  onOpenAgentConsignment,
  onCommitCustomOption,
  onRefreshParties,
  onSealUpload,
  onSealError,
  onClearPageMessages,
  onLetterOfCreditBusyChange,
}: InvoiceEditorDocumentSectionsProps) {
  const { validationMessage, revealInvalidField, clearValidationMessage } = useDocumentEditorValidation(activeSection, onNavigate, readInvoiceEditorSection);
  return (
    <div className="document-editor-sections" onInvalidCapture={revealInvalidField} onInputCapture={clearValidationMessage}>
      <InvoiceEditorNavigation
        invoiceNo={invoice.invoiceNo || ""}
        isNew={invoiceId <= 0}
        itemCount={invoice.items.filter(isMeaningfulInvoiceItem).length}
        totalLabel={formatAmount(invoice.totalAmount, invoice.currency)}
        activeSection={activeSection}
        editable={isEditable}
        busy={isBusy}
        saving={isSaving}
        hasUnsavedChanges={hasUnsavedChanges}
        onNavigate={onNavigate}
        onUppercase={onUppercase}
      />
      {validationMessage && <InlineNotice tone="error" title="请完善后保存">{validationMessage}</InlineNotice>}

      <InvoiceEditorSection id="header" activeSection={activeSection}>
        <InvoiceBasicInfoPanel
          invoice={invoice}
          canOpenSingleWindowDocuments={canOpenSingleWindowDocuments}
          canCloneInvoiceType={canCloneInvoiceType}
          cloneInvoiceTypeLabel={cloneInvoiceTypeLabel}
          canUnverifyInvoice={canUnverifyInvoice}
          canTransitionStatus={canTransitionStatus}
          canCancelStatus={canCancelStatus}
          isEditable={isEditable}
          isBusy={isBusy}
          isCloneInvoiceTypeBusy={cloneInvoiceTypeBusy}
          isUnverifyInvoiceBusy={unverifyInvoiceBusy}
          isTransitionStatusBusy={transitionStatusBusy}
          onTransitionStatus={onTransitionStatus}
          onCancelStatus={onCancelStatus}
          onChange={onChange}
          onCloneInvoiceType={onCloneInvoiceType}
          onUnverifyInvoice={onUnverifyInvoice}
          onOpenCustomsCoo={onOpenCustomsCoo}
          onOpenAgentConsignment={onOpenAgentConsignment}
          customOptions={customOptions}
          onCommitCustomOption={onCommitCustomOption}
          statusHistory={statusHistory}
          statusHistoryLoading={statusHistoryLoading}
          statusHistoryMessage={statusHistoryMessage}
        />

        <InvoicePartiesPanel
          invoice={invoice}
          client={client}
          selectedCustomer={selectedCustomer}
          selectedExporter={selectedExporter}
          isEditable={isEditable}
          isBusy={partyBusy}
          message={partyMessage}
          canManageExporterSeals={canManageExporterSeals}
          sealBusy={sealBusy}
          onRefresh={onRefreshParties}
          onChange={onChange}
          onSealUpload={onSealUpload}
          onSealError={onSealError}
        />
      </InvoiceEditorSection>
      <InvoiceEditorSection id="items" activeSection={activeSection}>{itemsPanel}</InvoiceEditorSection>
      <InvoiceEditorSection id="shipping" activeSection={activeSection}>
        <InvoiceShippingTermsPanel
          invoice={invoice}
          isEditable={isEditable}
          customOptions={customOptions}
          onChange={onChange}
          onCommitCustomOption={onCommitCustomOption}
        />

        <details className="invoice-new-optional-section information-tier-advanced">
          <summary>
            <span>报关与扩展字段（低频）</span>
            <small>报关行和低频自定义备注，按需展开</small>
          </summary>
          <InvoiceExtendedFieldsPanel
            invoice={invoice}
            isEditable={isEditable && canUseAdvancedTools}
            onChange={onChange}
          />
        </details>
      </InvoiceEditorSection>

      <InvoiceEditorSection id="analysis" activeSection={activeSection}>
        <InvoiceProfitAnalysisPanel
          client={client}
          invoice={invoice}
          invoiceId={invoiceId}
          disabled={profitAnalysisDisabled}
        />

        <InvoiceLetterOfCreditPanel
          client={client}
          invoice={invoice}
          disabled={letterOfCreditDisabled}
          reviewDisabled={letterOfCreditReviewDisabled}
          onChange={onChange}
          onClearPageMessages={onClearPageMessages}
          onBusyChange={onLetterOfCreditBusyChange}
        />
      </InvoiceEditorSection>

      <InvoiceEditorSection id="report" activeSection={activeSection}>
        <InvoiceReportPreviewPanel
          client={client}
          invoiceId={reportInvoiceId}
          invoiceDraft={invoiceDraft}
          invoiceNo={invoice.invoiceNo}
          customerName={invoice.customerNameEN}
          defaultToAddress={selectedCustomerEmail}
          hasUnsavedDraftChanges={hasUnsavedChanges}
        />
      </InvoiceEditorSection>
    </div>
  );
}
