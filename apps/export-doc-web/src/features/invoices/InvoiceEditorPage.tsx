import { EditorWorkspace } from "../../ui/layout/Workspaces.tsx";
import { DocumentFieldLabelsProvider } from "../../ui/DocumentFieldLabelsContext.tsx";
import { ArrowLeft, Edit3, Trash2 } from "lucide-react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { ServerDraftUpdateNotice } from "../../ui/serverDraftSync.tsx";
import { ConcurrencyConflictNotice, InlineNotice, PageState, PermissionNotice } from "../../ui/PageState.tsx";
import { WorkspaceDeviceNotice } from "../../ui/WorkspaceDeviceNotice.tsx";
import { InvoiceMarksAndItemsPanel } from "./InvoiceMarksAndItemsPanel.tsx";
import { InvoiceStatusReasonDialog } from "./InvoiceStatusReasonDialog.tsx";
import { canDeleteInvoiceStatus, getNextInvoiceStatus, normalizeInvoiceStatus } from "./invoiceModel.ts";
import { InvoiceEditorDocumentSections } from "./InvoiceEditorDocumentSections.tsx";
import { readInvoiceItemSpareColumnCount } from "./invoiceEditorHelpers.ts";
import { InvoiceReviewPanel } from "./InvoiceReviewPanel.tsx";
import { useInvoiceEditorWorkspace } from "./hooks/useInvoiceEditorWorkspace.ts";

export function InvoiceEditorPage({
  businessDate,
  client,
  mode,
  attachmentsEnabled = false,
}: {
  businessDate: string;
  client: ExportDocManagerApiClient;
  mode: "new" | "edit";
  attachmentsEnabled?: boolean;
}) {
  const {
    masterDataPermission, reportDesignPermission, navigate, formRef,
    activeSection, workspaceDeviceMode, workspaceDeviceCapabilities, invoice, invoicePermission,
    singleWindowPermission, message, setMessage, successMessage, setSuccessMessage,
    concurrencyMessage, setIsLetterOfCreditBusy, persistedInvoiceStatus, cancelReason, setCancelReason,
    isCancelReasonDialogOpen, parsedInvoiceId, isNew, isInvoiceIdValid, isInvoiceEditable,
    itemsWorkspace, invoiceQuery, statusHistoryQuery, selectedCustomerQuery, selectedExporterQuery,
    settingsQuery, cloneInvoiceTypeMutation, exporterSealMutation, refreshParties, saveInvoiceMutation,
    statusTransitionMutation, unverifyInvoiceMutation, products, units, invoiceCustomOptions,
    selectedCustomerEmail, isBusy, isPartyBusy, partyMessage, productMessage,
    unitLookupMessage, isProductLibraryBusy, invoiceItemBlankRowCount, cloneInvoiceTypeLabel, canUnverifyInvoice,
    currentInvoiceDraft, hasUnsavedInvoiceChanges, serverDraftSync, confirmDiscardChanges, patchInvoice,
    handleHsKnowledgeFeedback, uppercaseInvoiceText, commitInvoiceCustomOption, clearInvoicePageMessages, handleCloneInvoiceType,
    handleUnverifyInvoice, handleTransitionInvoiceStatus, closeCancelReasonDialog, confirmCancelReasonDialog, handleDeleteInvoice,
    handleSubmit, handleBackToInvoiceList, handleOpenCustomsCoo, handleOpenAgentConsignment, handleReloadLatestInvoice,
    navigateInvoiceSection,
  } = useInvoiceEditorWorkspace({ businessDate, client, mode, attachmentsEnabled });

  const invoiceItemsPanel = invoice ? (
    <InvoiceMarksAndItemsPanel
      key={invoice.id}
      client={client}
      invoice={invoice}
      canSaveToProductLibrary={masterDataPermission.canOperate}
      canUseHsKnowledge={invoicePermission.canOperate && workspaceDeviceCapabilities.canUseDenseWorkbench}
      canRedoItemEdit={itemsWorkspace.canRedoItemEdit}
      canUndoItemEdit={itemsWorkspace.canUndoItemEdit}
      invoiceItemBlankRowCount={invoiceItemBlankRowCount}
      defaultSpareColumnCount={readInvoiceItemSpareColumnCount(settingsQuery.data?.settings)}
      isEditable={isInvoiceEditable && workspaceDeviceCapabilities.canUseDenseWorkbench}
      isProductLibraryBusy={isProductLibraryBusy}
      onChange={patchInvoice}
      onHsKnowledgeFeedback={handleHsKnowledgeFeedback}
      onAddItem={itemsWorkspace.addItem}
      onApplyProductLibraryItem={itemsWorkspace.applyProductLibraryItem}
      onChangeItem={itemsWorkspace.patchItem}
      onEditItems={itemsWorkspace.editItems}
      onClearItemCells={itemsWorkspace.clearItemCells}
      onDuplicateItem={itemsWorkspace.duplicateItem}
      onFillDownItemCells={itemsWorkspace.fillDownItemCells}
      onFillDownItemField={itemsWorkspace.fillDownItemField}
      onMoveItem={itemsWorkspace.moveItem}
      onPasteItemTable={itemsWorkspace.pasteItemTable}
      onRedoItemEdit={itemsWorkspace.redoItemEdit}
      onRefreshProductLibrary={itemsWorkspace.refreshProductLibrary}
      onOpenProductLibrary={itemsWorkspace.openProductLibrary}
      onRemoveItem={itemsWorkspace.removeItem}
      onSaveItemToProductLibrary={itemsWorkspace.saveItemToProductLibrary}
      onSearchProductLibrary={itemsWorkspace.searchProductLibrary}
      onUndoItemEdit={itemsWorkspace.undoItemEdit}
      productLibraryMessage={productMessage}
      productLibraryProducts={products}
      productLibraryPageNumber={itemsWorkspace.productLibraryPageNumber}
      productLibraryPageSize={itemsWorkspace.productLibraryPageSize}
      productLibraryTotalCount={itemsWorkspace.productLibraryTotalCount}
      productLibraryTotalPages={itemsWorkspace.productLibraryTotalPages}
      onProductLibraryPageChange={itemsWorkspace.setProductLibraryPageNumber}
      onProductLibraryPageSizeChange={itemsWorkspace.changeProductLibraryPageSize}
      unitLookupMessage={unitLookupMessage}
      unitOptions={units}
    />
  ) : null;

  return (
    <DocumentFieldLabelsProvider value={settingsQuery.data?.settings.system.documentFieldLabels}>
    <EditorWorkspace className="editor-surface" aria-label={isNew ? "新建发票" : "编辑发票"}>
      <div className="editor-toolbar">
        <button className="command-button secondary" type="button" onClick={handleBackToInvoiceList}>
          <ArrowLeft size={17} aria-hidden="true" />
          <span>返回列表</span>
        </button>
        <div className="editor-title">
          <Edit3 size={18} aria-hidden="true" />
          <span>{isNew ? "新建发票" : invoice?.invoiceNo || "编辑发票"}</span>
          {invoice ? (
            <span
              className="editor-save-state"
              data-state={saveInvoiceMutation.isPending ? "saving" : hasUnsavedInvoiceChanges || isNew ? "dirty" : "saved"}
              role="status"
              aria-live="polite"
            >
              {saveInvoiceMutation.isPending ? "保存中" : hasUnsavedInvoiceChanges ? "有未保存修改" : isNew ? "尚未保存" : "已保存"}
            </span>
          ) : null}
        </div>
        {!isNew && isInvoiceIdValid && attachmentsEnabled && <button className="command-button secondary" type="button" disabled={isBusy}
          onClick={() => void confirmDiscardChanges("打开业务资料").then((accepted) => { if (accepted) navigate(`/invoices/${parsedInvoiceId}/attachments`); })}>业务资料</button>}
        {!isNew
          && isInvoiceIdValid
          && invoicePermission.canManage
          && canDeleteInvoiceStatus(persistedInvoiceStatus || invoice?.status) ? (
          <button
            className="command-button secondary danger"
            type="button"
            disabled={isBusy || !invoice}
            onClick={handleDeleteInvoice}
          >
            <Trash2 size={17} aria-hidden="true" />
            <span>删除</span>
          </button>
        ) : null}
      </div>

      {concurrencyMessage ? <ConcurrencyConflictNotice message={concurrencyMessage} isBusy={invoiceQuery.isFetching} onReload={() => void handleReloadLatestInvoice()} /> : null}
      {serverDraftSync.hasPendingServerVersion ? <ServerDraftUpdateNotice
        entityLabel="发票"
        onKeepLocal={serverDraftSync.keepLocalDraft}
        onLoadServer={serverDraftSync.loadServerVersion}
      /> : null}
      {message ? <InlineNotice tone="error" title="操作未完成">{message}</InlineNotice> : null}
      {settingsQuery.isError && <InlineNotice tone="warning" title="发票默认设置读取失败">{readApiError(settingsQuery.error)}；当前使用内置显示列，可稍后刷新。</InlineNotice>}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      {!invoicePermission.canOperate ? (
        <PermissionNotice>
          当前账号对这张发票没有编辑权限；表头、商品明细、信用证导入和保存已禁用，其它操作按各自权限开放。
        </PermissionNotice>
      ) : null}
      <WorkspaceDeviceNotice
        mode={workspaceDeviceMode}
        phone="手机端用于查看、搜索、审批和简单回填；商品明细编辑、批量录入、信用证处理和导入导出请使用桌面端。"
        tablet={workspaceDeviceCapabilities.canUseAdvancedTools
          ? "可进行轻量编辑、信用证处理和导入导出；商品明细编辑与批量录入仍需更宽屏幕。"
          : "平板端用于轻量编辑和现场确认；连接鼠标或触控板后可使用信用证处理和导入导出。"}
      />

      {!invoice && isBusy ? <PageState tone="loading" title="正在加载发票" description="请稍候，系统正在读取发票和商品明细。" /> : null}

      {invoice && isInvoiceEditable && <InvoiceReviewPanel client={client} invoice={invoice} disabled={isBusy} hasUnsavedChanges={hasUnsavedInvoiceChanges} />}

      {invoice ? (
        <form
          ref={formRef}
          className="invoice-form"
          onSubmit={handleSubmit}
          onKeyDownCapture={handleEnterAsTabFormKeyDown}
        >
          <InvoiceEditorDocumentSections
            key={isNew ? "new" : parsedInvoiceId}
            client={client}
            invoice={invoice}
            invoiceId={isNew ? 0 : parsedInvoiceId}
            activeSection={activeSection}
            reportInvoiceId={isNew || !isInvoiceIdValid ? 0 : parsedInvoiceId}
            invoiceDraft={currentInvoiceDraft ?? undefined}
            selectedCustomer={selectedCustomerQuery.data}
            selectedExporter={selectedExporterQuery.data}
            selectedCustomerEmail={selectedCustomerEmail}
            customOptions={invoiceCustomOptions}
            statusHistory={statusHistoryQuery.data}
            statusHistoryLoading={statusHistoryQuery.isFetching}
            statusHistoryMessage={statusHistoryQuery.isError ? readApiError(statusHistoryQuery.error) : null}
            itemsPanel={invoiceItemsPanel}
            cloneInvoiceTypeLabel={cloneInvoiceTypeLabel}
            isEditable={isInvoiceEditable}
            isBusy={isBusy}
            isSaving={saveInvoiceMutation.isPending}
            hasUnsavedChanges={hasUnsavedInvoiceChanges}
            canOpenSingleWindowDocuments={!isNew && isInvoiceIdValid && singleWindowPermission.canOperate}
            canCloneInvoiceType={!isNew && isInvoiceIdValid && invoicePermission.canOperate}
            canUnverifyInvoice={invoicePermission.canManage && canUnverifyInvoice}
            canTransitionStatus={!isNew && isInvoiceIdValid && invoicePermission.canOperate && Boolean(getNextInvoiceStatus(invoice.status))}
            canCancelStatus={!isNew && isInvoiceIdValid && invoicePermission.canManage && normalizeInvoiceStatus(invoice.status) !== "Cancelled"}
            canUseAdvancedTools={workspaceDeviceCapabilities.canUseAdvancedTools}
            canManageExporterSeals={isInvoiceEditable}
            cloneInvoiceTypeBusy={cloneInvoiceTypeMutation.isPending}
            unverifyInvoiceBusy={unverifyInvoiceMutation.isPending}
            transitionStatusBusy={statusTransitionMutation.isPending}
            partyBusy={isPartyBusy}
            partyMessage={partyMessage}
            sealBusy={exporterSealMutation.isPending}
            profitAnalysisDisabled={!invoicePermission.canOperate || invoiceQuery.isFetching || saveInvoiceMutation.isPending}
            letterOfCreditDisabled={!isInvoiceEditable || !workspaceDeviceCapabilities.canUseAdvancedTools || !reportDesignPermission.canOperate || invoiceQuery.isFetching || saveInvoiceMutation.isPending}
            letterOfCreditReviewDisabled={!invoicePermission.canOperate || !reportDesignPermission.canOperate || invoiceQuery.isFetching || saveInvoiceMutation.isPending}
            onNavigate={navigateInvoiceSection}
            onUppercase={uppercaseInvoiceText}
            onChange={patchInvoice}
            onTransitionStatus={() => void handleTransitionInvoiceStatus()}
            onCancelStatus={() => void handleTransitionInvoiceStatus("Cancelled")}
            onCloneInvoiceType={handleCloneInvoiceType}
            onUnverifyInvoice={handleUnverifyInvoice}
            onOpenCustomsCoo={handleOpenCustomsCoo}
            onOpenAgentConsignment={handleOpenAgentConsignment}
            onCommitCustomOption={commitInvoiceCustomOption}
            onRefreshParties={() => void refreshParties()}
            onSealUpload={(sealType, file) => exporterSealMutation.mutate({ sealType, file })}
            onSealError={(error) => {
              setMessage(readApiError(error));
              setSuccessMessage(null);
            }}
            onClearPageMessages={clearInvoicePageMessages}
            onLetterOfCreditBusyChange={setIsLetterOfCreditBusy}
          />
        </form>
      ) : null}
      {isCancelReasonDialogOpen ? (
        <InvoiceStatusReasonDialog
          title="填写作废原因"
          description="原因会写入发票状态审计记录，便于后续核查。"
          value={cancelReason}
          isBusy={statusTransitionMutation.isPending}
          onChange={setCancelReason}
          onCancel={closeCancelReasonDialog}
          onConfirm={confirmCancelReasonDialog}
        />
      ) : null}
    </EditorWorkspace>
    </DocumentFieldLabelsProvider>
  );
}
