import { ArrowLeft, Edit3, Trash2 } from "lucide-react";
import { useParams } from "react-router-dom";
import { EditorWorkspace } from "../../ui/layout/Workspaces.tsx";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { ServerDraftUpdateNotice } from "../../ui/serverDraftSync.tsx";
import { ConcurrencyConflictNotice, InlineNotice, PageState, PermissionNotice } from "../../ui/PageState.tsx";
import { DocumentFieldLabelsProvider } from "../../ui/DocumentFieldLabelsContext.tsx";
import { PaymentAmountsPanel, PaymentBasicInfoPanel, PaymentBusinessInfoPanel } from "./PaymentFormPanels.tsx";
import { PaymentReportPreviewPanel } from "./PaymentReportPreviewPanel.tsx";
import { PaymentEditorSections } from "./PaymentEditorSections.tsx";
import { usePaymentEditorWorkspace, type PaymentEditorProps } from "./usePaymentEditorWorkspace.ts";

export function PaymentEditorPage(props: PaymentEditorProps) {
  const { paymentId } = useParams();
  // A different record owns a different draft, including while its request fails or is pending.
  return <PaymentEditor key={`${props.mode}:${paymentId ?? ""}`} {...props} paymentId={paymentId} />;
}

function PaymentEditor({ paymentId, ...props }: PaymentEditorProps & { paymentId?: string }) {
  const { client } = props;
  const { isNew, parsedPaymentId, isPaymentIdValid, payment, paymentPermission, masterDataPermission,
    message, successMessage, concurrencyMessage, settingsQuery, paymentQuery, serverDraftSync,
    formRef, isBusy, commands, hasUnsavedPaymentChanges, currentPaymentDraft,
    isReferenceDataBusy, selectedPayeeQuery, payerNameOptions, referenceDataMessage, paymentCustomOptions,
    handleBackToPaymentList, handleDeletePayment, handleReloadLatestPayment, handleSubmit,
    patchPayment, patchPaymentAmounts, commitPaymentCustomOption, handleOpenPayeeManagement, refreshReferenceData } = usePaymentEditorWorkspace(props, paymentId);
  return (
    <EditorWorkspace className="editor-surface" aria-label={isNew ? "新建付款报销" : "编辑付款报销"}>
      <div className="editor-toolbar">
        <button className="command-button secondary" type="button" onClick={handleBackToPaymentList}>
          <ArrowLeft size={17} aria-hidden="true" />
          <span>返回列表</span>
        </button>
        <div className="editor-title">
          <Edit3 size={18} aria-hidden="true" />
          <span>{isNew ? "新建付款报销" : payment?.voucherNo || payment?.invoiceNo || "编辑付款报销"}</span>
        </div>
        {!isNew && isPaymentIdValid && paymentPermission.canManage ? (
          <button
            className="command-button secondary danger"
            type="button"
            disabled={isBusy || !payment}
            onClick={handleDeletePayment}
          >
            <Trash2 size={17} aria-hidden="true" />
            <span>删除</span>
          </button>
        ) : null}
      </div>

      {concurrencyMessage ? <ConcurrencyConflictNotice message={concurrencyMessage} isBusy={paymentQuery.isFetching} onReload={() => void handleReloadLatestPayment()} /> : null}
      {serverDraftSync.hasPendingServerVersion ? <ServerDraftUpdateNotice
        entityLabel="付款记录"
        onKeepLocal={serverDraftSync.keepLocalDraft}
        onLoadServer={serverDraftSync.loadServerVersion}
      /> : null}
      {message ? <InlineNotice tone="error" title="操作未完成">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      {!paymentPermission.canOperate ? (
        <PermissionNotice>当前账号对这笔付款报销没有编辑权限；表单修改和保存已禁用，删除和输出按各自权限控制。</PermissionNotice>
      ) : null}

      {settingsQuery.isError && <InlineNotice tone="warning" title="字段名称未能更新" action={<button className="command-button secondary" type="button" onClick={() => void settingsQuery.refetch()}>重试</button>}>{readApiError(settingsQuery.error)}</InlineNotice>}
      {!payment && isBusy ? <PageState tone="loading" title="正在加载付款报销" description="正在读取付款信息和报表配置。" /> : null}

      {payment ? (
        <DocumentFieldLabelsProvider value={settingsQuery.data?.settings.system.documentFieldLabels}>
          <form ref={formRef} className="entity-form" onSubmit={handleSubmit} onKeyDownCapture={handleEnterAsTabFormKeyDown}>
            <PaymentEditorSections key={isNew ? "new" : parsedPaymentId} payment={payment} isNew={isNew} busy={isBusy}
              saving={commands.isPending} editable={paymentPermission.canOperate} hasUnsavedChanges={hasUnsavedPaymentChanges}
              basic={
                <PaymentBasicInfoPanel
                  payment={payment}
                  client={client}
                  isBusy={isBusy}
                  isReferenceDataBusy={isReferenceDataBusy}
                  selectedPayee={selectedPayeeQuery.data}
                  payerNameOptions={payerNameOptions}
                  referenceDataMessage={referenceDataMessage}
                  customOptions={paymentCustomOptions}
                  onChange={patchPayment}
                  onCommitCustomOption={commitPaymentCustomOption}
                  onOpenPayeeManagement={handleOpenPayeeManagement}
                  canOpenPayeeManagement={masterDataPermission.canView}
                  onRefreshReferenceData={refreshReferenceData}
                />
              }
              business={<PaymentBusinessInfoPanel payment={payment} onChange={patchPayment} />}
              amounts={<PaymentAmountsPanel payment={payment} onChange={patchPaymentAmounts} />}
              report={
                <PaymentReportPreviewPanel
                  client={client}
                  paymentId={isNew || !isPaymentIdValid ? 0 : parsedPaymentId}
                  paymentDraft={currentPaymentDraft}
                  hasUnsavedDraftChanges={hasUnsavedPaymentChanges}
                />
              } />
          </form>
        </DocumentFieldLabelsProvider>
      ) : null}
    </EditorWorkspace>
  );
}
