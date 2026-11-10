import { EditorWorkspace } from "../../ui/layout/Workspaces.tsx";
import { FormEvent, useMemo, useState } from "react";
import "../../styles/routes/single-window.css";
import { useQuery } from "@tanstack/react-query";
import { ClipboardList } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import type { ApiAgentConsignmentDocumentDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { queryKeys } from "../../api/queryKeys.ts";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { formatPlainNumber, readApiError } from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { ServerDraftUpdateNotice, useServerDraftSync } from "../../ui/serverDraftSync.tsx";
import { InlineNotice, PageState, PermissionNotice } from "../../ui/PageState.tsx";
import { SingleWindowHandoffPanel } from "./SingleWindowHandoffPanel.tsx";
import { SingleWindowLockedFieldsDialog } from "./SingleWindowLockedFieldsDialog.tsx";
import { SingleWindowExportReviewPanel } from "./SingleWindowExportReviewPanel.tsx";
import { SingleWindowScopedClearControls } from "./SingleWindowScopedClearControls.tsx";
import { SingleWindowDocumentActionBar } from "./SingleWindowDocumentActionBar.tsx";
import { SingleWindowSectionNav } from "./SingleWindowSectionNav.tsx";
import { SingleWindowTabs } from "./SingleWindowNavigation.tsx";
import {
  AgentConsignmentDocumentsPanel,
  AgentConsignmentReceiptPanel,
  AgentConsignmentSummary,
  AgentConsignmentWorkbench,
} from "./AgentConsignmentPanels.tsx";
import { useSingleWindowDocumentWorkspace } from "./useSingleWindowDocumentWorkspace.ts";
import { useSingleWindowLockedFields } from "./useSingleWindowLockedFields.ts";
import {
  buildAgentConsignmentDocumentSnapshot,
  buildAgentConsignmentEditorOptions,
  buildAgentConsignmentSectionNavItems,
  formatScopedClearResultMessage,
  normalizeAgentConsignmentDocumentForSave,
} from "./agentConsignmentModel.ts";
import {
  agentScopedClearOptionsByGroup,
  applyAgentDefaultsForScope,
  applyAgentDefaultsToEmptyFields,
  areEditorDocumentsEqual,
  clearAgentManualOverrides,
  cloneEditorDocument,
} from "./singleWindowEditorTools.ts";

const agentConsignmentBusinessType = "AgentConsignment";
const agentScopedClearGroups = [
  { key: "基础标识", label: "基础标识" },
  { key: "申报要素", label: "申报要素" },
  { key: "单证与费用", label: "单证与费用" },
] as const;
export function AgentConsignmentPage({ client }: { client: ExportDocManagerApiClient }) {
  const permission = useModulePermission("document.single-window");
  const { sourceId } = useParams();
  const navigate = useNavigate();
  const parsedInvoiceId = Number(sourceId);
  const isInvoiceIdValid = Number.isInteger(parsedInvoiceId) && parsedInvoiceId > 0;
  const documentQueryKey = queryKeys.singleWindowAgentConsignmentDocument(parsedInvoiceId);
  const reviewQueryKey = queryKeys.singleWindowAgentConsignmentExportReview(parsedInvoiceId);

  const [document, setDocument] = useState<ApiAgentConsignmentDocumentDto | null>(null);
  const [undoDocument, setUndoDocument] = useState<ApiAgentConsignmentDocumentDto | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [persistedDocumentSnapshot, setPersistedDocumentSnapshot] = useState<string | null>(null);

  const documentQuery = useQuery({
    queryKey: documentQueryKey,
    queryFn: ({ signal }) => client.getAgentConsignmentDocument({ sourceId: parsedInvoiceId }, { signal }),
    enabled: isInvoiceIdValid,
  });

  const reviewQuery = useQuery({
    queryKey: reviewQueryKey,
    queryFn: ({ signal }) =>
      client.getSingleWindowExportReview({
        businessType: agentConsignmentBusinessType,
        sourceId: parsedInvoiceId,
      }, { signal }),
    enabled: isInvoiceIdValid,
  });

  const referenceCatalogQuery = useQuery({
    queryKey: queryKeys.singleWindowReferenceCatalog(),
    queryFn: ({ signal }) => client.getSingleWindowReferenceCatalog({ signal }),
    staleTime: 5 * 60 * 1000,
  });

  const workspace = useSingleWindowDocumentWorkspace({
    scope: String(parsedInvoiceId), owner: client, enabled: isInvoiceIdValid && permission.canOperate,
    document, setDocument, setUndoDocument, setMessage, setSuccessMessage, setPersistedDocumentSnapshot,
    documentQueryKey, reviewQueryKey,
    snapshot: value => buildAgentConsignmentDocumentSnapshot(value, parsedInvoiceId),
    saveDocument: (value, signal) => client.saveAgentConsignmentDocument({ sourceId: parsedInvoiceId, body: normalizeAgentConsignmentDocumentForSave(value, parsedInvoiceId) }, { signal }),
    getDocument: signal => client.getAgentConsignmentDocument({ sourceId: parsedInvoiceId }, { signal }),
    buildDefaults: signal => client.buildAgentConsignmentDefaults({ sourceId: parsedInvoiceId }, { signal }),
    repair: (groupKeys, signal) => client.repairSingleWindowExportReviewGroups({ businessType: agentConsignmentBusinessType, sourceId: parsedInvoiceId, body: { groupKeys } }, { signal }),
    fillEmpty: applyAgentDefaultsToEmptyFields,
    clearScope: applyAgentDefaultsForScope,
    clearManual: clearAgentManualOverrides,
    clearMessage: formatScopedClearResultMessage,
  });
  const lockedFieldsWorkspace = useSingleWindowLockedFields({
    workspace, document,
    hasUnsavedChanges: Boolean(document && documentQuery.data && !areEditorDocumentsEqual(document, documentQuery.data)),
    loadLockedFields: signal => client.getAgentConsignmentLockedFields({ sourceId: parsedInvoiceId }, { signal }),
    unlockFields: (fieldKeys, signal) => client.unlockAgentConsignmentFields({ sourceId: parsedInvoiceId, body: { fieldKeys } }, { signal }),
  });

  const isBusy =
    documentQuery.isFetching ||
    reviewQuery.isFetching ||
    workspace.isPending;
  const loadMessage = !isInvoiceIdValid
    ? "申报资料 无效。"
    : documentQuery.isError
      ? readApiError(documentQuery.error)
      : null;
  const reviewMessage = reviewQuery.isError ? readApiError(reviewQuery.error) : null;
  const referenceMessage = referenceCatalogQuery.isError ? readApiError(referenceCatalogQuery.error) : null;
  const agentEditorOptions = useMemo(
    () => buildAgentConsignmentEditorOptions(referenceCatalogQuery.data?.catalog),
    [referenceCatalogQuery.data?.catalog],
  );
  const currentDocumentSnapshot = useMemo(
    () => (document && isInvoiceIdValid ? buildAgentConsignmentDocumentSnapshot(document, parsedInvoiceId) : null),
    [document, isInvoiceIdValid, parsedInvoiceId],
  );
  const hasUnsavedDocumentChanges = Boolean(
    permission.canOperate &&
    document &&
      persistedDocumentSnapshot &&
      currentDocumentSnapshot &&
      currentDocumentSnapshot !== persistedDocumentSnapshot,
  );
  const serverDraftSync = useServerDraftSync({
    resourceKey: parsedInvoiceId,
    incomingValue: documentQuery.data,
    persistedValue: workspace.persistedDocument,
    isDirty: hasUnsavedDocumentChanges,
    fingerprint: (serverDocument) => buildAgentConsignmentDocumentSnapshot(serverDocument, parsedInvoiceId),
    applyIncoming: (serverDocument) => {
      setDocument(serverDocument);
      setPersistedDocumentSnapshot(buildAgentConsignmentDocumentSnapshot(serverDocument, parsedInvoiceId));
      setUndoDocument(null);
      setMessage(null);
    },
  });
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: hasUnsavedDocumentChanges,
    message: "当前代理委托草稿有未保存的修改。",
  });

  function patchDocument(next: Partial<ApiAgentConsignmentDocumentDto>) {
    setDocument(current => current ? { ...current, ...next } : current);
    setUndoDocument(null); setSuccessMessage(null);
  }
  const handleRepairReviewGroups = (groupKeys: string[]) => workspace.repair(groupKeys, hasUnsavedDocumentChanges);
  const handleRestoreDefaults = workspace.restore;
  const handleFillEmptyFields = workspace.fillEmpty;
  const handleClearManualOverrides = workspace.clearManual;
  const handleClearScopedGroup = workspace.clearScope;
  const handleClearScopedCategory = workspace.clearScope;

  function handleUndoToolAction() {
    if (!undoDocument) {
      return;
    }

    setDocument(cloneEditorDocument(undoDocument));
    setUndoDocument(null);
    setMessage(null);
    setSuccessMessage("已撤销上一次工具动作，保存后写入草稿。");
  }

  async function handleBackToInvoice() {
    if (await confirmDiscardChanges("申报资料")) {
      navigate("/single-window/sources");
    }
  }

  async function handleRefreshDocument() {
    if (await confirmDiscardChanges("刷新草稿")) {
      void documentQuery.refetch();
    }
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!permission.canOperate || !document || !isInvoiceIdValid) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    void workspace.save();
  }

  return (
    <EditorWorkspace className="editor-surface agent-consignment-surface" aria-label="报关代理委托草稿">
      <SingleWindowTabs activeKey="agent-consignment" />

      <SingleWindowDocumentActionBar
        title={document ? document.invoiceNo || `发票 ${parsedInvoiceId}` : "报关代理委托草稿"}
        titleIcon={<ClipboardList size={18} aria-hidden="true" />}
        formId="agent-consignment-form"
        isBusy={isBusy}
        isDocumentReady={Boolean(document)}
        isInvoiceIdValid={isInvoiceIdValid}
        canOperate={permission.canOperate}
        canUndo={Boolean(undoDocument)}
        scopedClearControls={
          <SingleWindowScopedClearControls
            groups={agentScopedClearGroups}
            optionsByGroup={agentScopedClearOptionsByGroup}
            disabled={!permission.canOperate || isBusy || !document || !isInvoiceIdValid}
            onClearGroup={handleClearScopedGroup}
            onClearCategory={handleClearScopedCategory}
          />
        }
        onBack={handleBackToInvoice}
        onRefresh={handleRefreshDocument}
        onRestoreDefaults={handleRestoreDefaults}
        onFillEmptyFields={handleFillEmptyFields}
        onClearManualOverrides={handleClearManualOverrides}
        onOpenLockedFields={lockedFieldsWorkspace.open}
        onUndo={handleUndoToolAction}
        onBuildReview={() => void reviewQuery.refetch()}
      />

      {loadMessage || message ? <InlineNotice tone="error" title="代理委托操作未完成">{loadMessage || message}</InlineNotice> : null}
      {serverDraftSync.hasPendingServerVersion ? <ServerDraftUpdateNotice
        entityLabel="代理委托草稿"
        onKeepLocal={serverDraftSync.keepLocalDraft}
        onLoadServer={serverDraftSync.loadServerVersion}
      /> : null}
      {referenceMessage ? <InlineNotice tone="warning" title="候选资料未完整加载">报关代理委托候选项加载失败：{referenceMessage}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      {!permission.canOperate ? <PermissionNotice>当前权限模板仅允许查看单一窗口草稿和预检结果；修改、修复、保存与交接操作已禁用。</PermissionNotice> : null}
      {!document && isBusy ? <PageState tone="loading" title="正在加载代理委托草稿" description="正在读取委托信息、商品明细和预检状态。" /> : null}

      {document ? (
        <form id="agent-consignment-form" className="entity-form agent-consignment-form" onSubmit={handleSubmit} onKeyDownCapture={handleEnterAsTabFormKeyDown}>
          <SingleWindowSectionNav
            items={buildAgentConsignmentSectionNavItems(document)}
            ariaLabel="代理委托录入分区"
          />

          <section id="acd-section-status" className="form-section single-window-editor-section" aria-label="草稿状态">
            <div className="section-header">
              <h2>草稿状态</h2>
              <span className="section-count">草稿版本 {formatPlainNumber(document.draftRevision)}</span>
            </div>
            <AgentConsignmentSummary document={document} />
          </section>

          <fieldset className="permission-fieldset" disabled={!permission.canOperate}>

          <section id="acd-section-basic" className="form-section single-window-editor-section" aria-label="报文与申报信息">
            <div className="section-header">
              <h2>报文与申报信息</h2>
            </div>
            <AgentConsignmentWorkbench
              document={document}
              editorOptions={agentEditorOptions}
              onPatchDocument={patchDocument}
            />
          </section>

          <section id="acd-section-documents" className="form-section single-window-editor-section" aria-label="单证与费用">
            <div className="section-header">
              <h2>单证与费用</h2>
            </div>
            <AgentConsignmentDocumentsPanel document={document} onPatchDocument={patchDocument} />
          </section>
          </fieldset>

          <section id="acd-section-receipt" className="form-section single-window-editor-section" aria-label="回执回写信息">
            <div className="section-header">
              <h2>回执回写信息</h2>
            </div>
            <AgentConsignmentReceiptPanel document={document} />
          </section>

          <section id="acd-section-review" className="form-section single-window-editor-section" aria-label="导出前预检">
            <div className="section-header">
              <h2>导出前预检</h2>
              <span className="section-count">
                {reviewQuery.data
                  ? `${reviewQuery.data.totalErrorCount} 错误 · ${reviewQuery.data.totalWarningCount} 警告`
                  : "未加载"}
              </span>
            </div>
            {reviewMessage ? <InlineNotice tone="warning" title="审查提示">{reviewMessage}</InlineNotice> : null}
            <SingleWindowExportReviewPanel
              review={reviewQuery.data ?? null}
              isBusy={reviewQuery.isFetching}
              isActionDisabled={!permission.canOperate || isBusy || !isInvoiceIdValid}
              repairBusy={workspace.isPending}
              onRepairGroups={handleRepairReviewGroups}
            />
          </section>

          <SingleWindowHandoffPanel businessType="AgentConsignment" client={client} sourceId={parsedInvoiceId} canOperate={permission.canOperate} dirty={hasUnsavedDocumentChanges} busy={isBusy} draftRevision={Number(document?.draftRevision ?? 0)} workspace={workspace} />
        </form>
      ) : null}

      {lockedFieldsWorkspace.isOpen ? (
        <SingleWindowLockedFieldsDialog
          title="代理委托字段锁定"
          fields={lockedFieldsWorkspace.fields}
          selectedKeys={lockedFieldsWorkspace.selectedFieldKeys}
          isBusy={lockedFieldsWorkspace.isPending}
          onClose={lockedFieldsWorkspace.close}
          onToggleField={lockedFieldsWorkspace.toggleField}
          onToggleAll={lockedFieldsWorkspace.toggleAll}
          onUnlockSelected={lockedFieldsWorkspace.unlockSelected}
        />
      ) : null}
    </EditorWorkspace>
  );
}
