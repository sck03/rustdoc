import { FormEvent, useEffect, useMemo, useState } from "react";
import "../../styles/routes/single-window.css";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ClipboardList } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import type { ApiAgentConsignmentDocumentDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { queryKeys } from "../../api/queryKeys.ts";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { formatPlainNumber, readApiError } from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { ServerDraftUpdateNotice, useServerDraftSync } from "../../ui/serverDraftSync.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
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
import { useSingleWindowLockedFields } from "./useSingleWindowLockedFields.ts";
import {
  type AgentScopedClearRequest,
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
  const requestConfirmation = useConfirmation();
  const { invoiceId } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const parsedInvoiceId = Number(invoiceId);
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
    queryFn: ({ signal }) => client.getAgentConsignmentDocument({ invoiceId: parsedInvoiceId }, { signal }),
    enabled: isInvoiceIdValid,
  });

  const reviewQuery = useQuery({
    queryKey: reviewQueryKey,
    queryFn: ({ signal }) =>
      client.getSingleWindowExportReview({
        businessType: agentConsignmentBusinessType,
        invoiceId: parsedInvoiceId,
      }, { signal }),
    enabled: isInvoiceIdValid,
  });

  const referenceCatalogQuery = useQuery({
    queryKey: queryKeys.singleWindowReferenceCatalog(),
    queryFn: ({ signal }) => client.getSingleWindowReferenceCatalog({ signal }),
    staleTime: 5 * 60 * 1000,
  });

  const buildDefaultsMutation = useMutation({
    mutationFn: (_snapshot: ApiAgentConsignmentDocumentDto) => client.buildAgentConsignmentDefaults({ invoiceId: parsedInvoiceId }),
    onSuccess: (nextDocument, snapshot) => {
      setDocument(nextDocument);
      setUndoDocument(areEditorDocumentsEqual(snapshot, nextDocument) ? null : snapshot);
      setMessage(null);
      setSuccessMessage(
        areEditorDocumentsEqual(snapshot, nextDocument)
          ? "当前已经是按发票推导的建议值，无需恢复默认值。"
          : "已恢复为按当前发票推导的默认值，保存后写入草稿。",
      );
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const fillEmptyMutation = useMutation({
    mutationFn: (_snapshot: ApiAgentConsignmentDocumentDto) => client.buildAgentConsignmentDefaults({ invoiceId: parsedInvoiceId }),
    onSuccess: (defaults, snapshot) => {
      const result = applyAgentDefaultsToEmptyFields(snapshot, defaults);
      setDocument(result.document);
      setUndoDocument(result.changedCount > 0 ? snapshot : null);
      setMessage(null);
      setSuccessMessage(
        result.changedCount > 0
          ? `已按当前发票回填 ${result.changedCount} 个空白项，保存后写入草稿。`
          : "当前可回填的空白项已经都补齐了。",
      );
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const scopedClearMutation = useMutation({
    mutationFn: async (request: AgentScopedClearRequest) => ({
      defaults: await client.buildAgentConsignmentDefaults({ invoiceId: parsedInvoiceId }),
      request,
    }),
    onSuccess: ({ defaults, request }) => {
      const result = applyAgentDefaultsForScope(
        request.snapshot,
        defaults,
        request.groupKey,
        request.categoryKey ?? "",
      );
      setDocument(result.document);
      setUndoDocument(result.changedCount > 0 ? request.snapshot : null);
      setMessage(null);
      setSuccessMessage(formatScopedClearResultMessage(request, result.changedCount));
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const repairReviewMutation = useMutation({
    mutationFn: async (request: { groupKeys: string[]; snapshot: ApiAgentConsignmentDocumentDto | null }) => {
      let savedBeforeRepair = false;
      if (request.snapshot) {
        await client.saveAgentConsignmentDocument({
          invoiceId: parsedInvoiceId,
          body: normalizeAgentConsignmentDocumentForSave(request.snapshot, parsedInvoiceId),
        });
        savedBeforeRepair = true;
      }

      const response = await client.repairSingleWindowExportReviewGroups({
        businessType: agentConsignmentBusinessType,
        invoiceId: parsedInvoiceId,
        body: { groupKeys: request.groupKeys },
      });
      const repairedDocument = await client.getAgentConsignmentDocument({ invoiceId: parsedInvoiceId });
      return { response, repairedDocument, savedBeforeRepair };
    },
    onSuccess: async ({ response, repairedDocument, savedBeforeRepair }) => {
      setDocument(repairedDocument);
      setPersistedDocumentSnapshot(buildAgentConsignmentDocumentSnapshot(repairedDocument, parsedInvoiceId));
      setUndoDocument(null);
      queryClient.setQueryData(documentQueryKey, repairedDocument);
      queryClient.setQueryData(reviewQueryKey, response.review);
      setMessage(null);
      setSuccessMessage(
        `${savedBeforeRepair ? "已先保存当前草稿，" : ""}${
          response.message || `已自动修复 ${response.repairedGroupCount} 个预检分组。`
        }`,
      );
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const saveMutation = useMutation({
    mutationFn: (body: ApiAgentConsignmentDocumentDto) =>
      client.saveAgentConsignmentDocument({
        invoiceId: parsedInvoiceId,
        body,
      }),
    onSuccess: async (response) => {
      setDocument(response.document);
      setPersistedDocumentSnapshot(buildAgentConsignmentDocumentSnapshot(response.document, parsedInvoiceId));
      setUndoDocument(null);
      queryClient.setQueryData(documentQueryKey, response.document);
      setMessage(null);
      setSuccessMessage(response.message || "代理委托草稿已保存。");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() }),
        queryClient.invalidateQueries({ queryKey: reviewQueryKey }),
      ]);
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const lockedFieldsWorkspace = useSingleWindowLockedFields({
    document,
    isDocumentValid: isInvoiceIdValid,
    hasUnsavedChanges: Boolean(document && documentQuery.data && !areEditorDocumentsEqual(document, documentQuery.data)),
    saveDocument: async () => {
      const response = await client.saveAgentConsignmentDocument({
        invoiceId: parsedInvoiceId,
        body: normalizeAgentConsignmentDocumentForSave(document!, parsedInvoiceId),
      });
      return response.document;
    },
    loadLockedFields: () => client.getAgentConsignmentLockedFields({ invoiceId: parsedInvoiceId }),
    unlockFields: (fieldKeys) => client.unlockAgentConsignmentFields({ invoiceId: parsedInvoiceId, body: { fieldKeys } }),
    applyPersistedDocument: (nextDocument) => {
      setDocument(nextDocument);
      setPersistedDocumentSnapshot(buildAgentConsignmentDocumentSnapshot(nextDocument, parsedInvoiceId));
      setUndoDocument(null);
      queryClient.setQueryData(documentQueryKey, nextDocument);
      void Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() }),
        queryClient.invalidateQueries({ queryKey: reviewQueryKey }),
      ]);
    },
    clearMessages: () => { setMessage(null); setSuccessMessage(null); },
    showError: (nextMessage) => { setMessage(nextMessage); setSuccessMessage(null); },
    showSuccess: (nextMessage) => { setMessage(null); setSuccessMessage(nextMessage); },
  });

  const isBusy =
    documentQuery.isFetching ||
    reviewQuery.isFetching ||
    buildDefaultsMutation.isPending ||
    fillEmptyMutation.isPending ||
    scopedClearMutation.isPending ||
    repairReviewMutation.isPending ||
    saveMutation.isPending ||
    lockedFieldsWorkspace.isPending;
  const loadMessage = !isInvoiceIdValid
    ? "发票 ID 无效。"
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

  async function handleRepairReviewGroups(groupKeys: string[]) {
    if (!permission.canOperate || !document || !isInvoiceIdValid || groupKeys.length === 0) {
      return;
    }

    const snapshot = cloneEditorDocument(document);
    const shouldSaveCurrentDraft =
      documentQuery.data != null && !areEditorDocumentsEqual(snapshot, documentQuery.data);

    if (
      shouldSaveCurrentDraft &&
      !await requestConfirmation({ title: "保存并自动修复", description: "当前代理委托草稿有未保存修改，需要先保存当前草稿再执行自动修复。", confirmLabel: "保存并修复" })
    ) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    repairReviewMutation.mutate({
      groupKeys,
      snapshot: shouldSaveCurrentDraft ? snapshot : null,
    });
  }

  function patchDocument(next: Partial<ApiAgentConsignmentDocumentDto>) {
    setDocument((current) => (current ? { ...current, ...next } : current));
    setUndoDocument(null);
    setSuccessMessage(null);
  }

  async function handleRestoreDefaults() {
    if (!document || !isInvoiceIdValid) {
      return;
    }

    if (!await requestConfirmation({ title: "重新套用建议值", description: "系统将按当前发票重新生成建议值。", details: ["原来的手工覆盖内容会被替换。"], confirmLabel: "重新套用" })) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    buildDefaultsMutation.mutate(cloneEditorDocument(document));
  }

  function handleFillEmptyFields() {
    if (!document || !isInvoiceIdValid) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    fillEmptyMutation.mutate(cloneEditorDocument(document));
  }

  async function handleClearManualOverrides() {
    if (!document || !isInvoiceIdValid) {
      return;
    }

    if (!await requestConfirmation({ title: "清空手工覆盖", description: "确定清空手工补充的覆盖字段吗？", details: ["系统回写值会保留。", "保存后才会写入草稿。"], confirmLabel: "清空覆盖" })) {
      return;
    }

    const snapshot = cloneEditorDocument(document);
    const result = clearAgentManualOverrides(snapshot);
    setDocument(result.document);
    setUndoDocument(result.changedCount > 0 ? snapshot : null);
    setMessage(null);
    setSuccessMessage(
      result.changedCount > 0
        ? "已清空手工补充的覆盖字段，系统回写值已保留，保存后写入草稿。"
        : "当前没有可清空的手工覆盖字段。",
    );
  }

  async function handleClearScopedGroup(groupKey: string) {
    if (!document || !isInvoiceIdValid) {
      return;
    }

    if (!await requestConfirmation({ title: "恢复分组建议值", description: `确定把“${groupKey}”分组里的手工覆盖值恢复到当前发票建议值吗？`, confirmLabel: "确认恢复" })) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    scopedClearMutation.mutate({ snapshot: cloneEditorDocument(document), groupKey });
  }

  async function handleClearScopedCategory(groupKey: string, categoryKey: string, categoryLabel: string) {
    if (!document || !isInvoiceIdValid) {
      return;
    }

    if (!await requestConfirmation({ title: "恢复分类建议值", description: `确定只恢复“${groupKey}”分组里的“${categoryLabel}”吗？`, confirmLabel: "确认恢复" })) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    scopedClearMutation.mutate({
      snapshot: cloneEditorDocument(document),
      groupKey,
      categoryKey,
      categoryLabel,
    });
  }

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
    if (await confirmDiscardChanges("返回发票")) {
      navigate(isInvoiceIdValid ? `/invoices/${parsedInvoiceId}` : "/invoices");
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
    saveMutation.mutate(normalizeAgentConsignmentDocumentForSave(document, parsedInvoiceId));
  }

  return (
    <section className="editor-surface agent-consignment-surface" aria-label="报关代理委托草稿">
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
              repairBusy={repairReviewMutation.isPending}
              onRepairGroups={handleRepairReviewGroups}
            />
          </section>

          <SingleWindowHandoffPanel businessType="AgentConsignment" client={client} invoiceId={parsedInvoiceId} canOperate={permission.canOperate} />
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
    </section>
  );
}
