import { FormEvent, ReactNode, useEffect, useId, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ClipboardList } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import {
  ApiAgentConsignmentDocumentDto,
  ApiSingleWindowLockedFieldDto,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { queryKeys } from "../../api/queryKeys.ts";
import { FieldShell, SelectField, TextAreaField, TextField } from "../../ui/FormFields.tsx";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { formatPlainNumber, readApiError } from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { PageState, PermissionNotice } from "../../ui/PageState.tsx";
import { SingleWindowHandoffPanel } from "./SingleWindowHandoffPanel.tsx";
import { SingleWindowLockedFieldsDialog } from "./SingleWindowLockedFieldsDialog.tsx";
import { SingleWindowExportReviewPanel } from "./SingleWindowExportReviewPanel.tsx";
import { SingleWindowScopedClearControls } from "./SingleWindowScopedClearControls.tsx";
import { SingleWindowDocumentActionBar } from "./SingleWindowDocumentActionBar.tsx";
import { SingleWindowSectionNav } from "./SingleWindowSectionNav.tsx";
import { SingleWindowTabs } from "./SingleWindowNavigation.tsx";
import {
  type AgentScopedClearRequest,
  buildAgentConsignmentDocumentSnapshot,
  buildAgentConsignmentEditorOptions,
  buildAgentConsignmentSectionNavItems,
  formatAgentDateTime,
  formatScopedClearResultMessage,
  normalizeAgentConsignmentDocumentForSave,
  readAgentDisplayText,
  readAgentDisplayValue,
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
const agentOperTypeOptions = [
  { value: "1", label: "1：新增" },
  { value: "2", label: "2：变更" },
  { value: "3", label: "3：删除" },
];
const agentPackingConditionOptions = ["纸箱", "托盘", "木箱", "裸装", "散装", "其他包装"];
const agentPaperInfoOptions = ["已收齐", "待补充", "发票", "装箱单", "合同", "提单", "报关委托书", "海关原产地证", "其他"];


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
  const [lockedFieldsOpen, setLockedFieldsOpen] = useState(false);
  const [lockedFields, setLockedFields] = useState<ApiSingleWindowLockedFieldDto[]>([]);
  const [selectedLockedFieldKeys, setSelectedLockedFieldKeys] = useState<Set<string>>(() => new Set());
  const [persistedDocumentSnapshot, setPersistedDocumentSnapshot] = useState<string | null>(null);

  const documentQuery = useQuery({
    queryKey: documentQueryKey,
    queryFn: () => client.getAgentConsignmentDocument({ invoiceId: parsedInvoiceId }),
    enabled: isInvoiceIdValid,
  });

  const reviewQuery = useQuery({
    queryKey: reviewQueryKey,
    queryFn: () =>
      client.getSingleWindowExportReview({
        businessType: agentConsignmentBusinessType,
        invoiceId: parsedInvoiceId,
      }),
    enabled: isInvoiceIdValid,
  });

  const referenceCatalogQuery = useQuery({
    queryKey: queryKeys.singleWindowReferenceCatalog(),
    queryFn: () => client.getSingleWindowReferenceCatalog(),
    staleTime: 5 * 60 * 1000,
  });

  useEffect(() => {
    if (documentQuery.data) {
      setDocument(documentQuery.data);
      setPersistedDocumentSnapshot(buildAgentConsignmentDocumentSnapshot(documentQuery.data, parsedInvoiceId));
      setUndoDocument(null);
      setMessage(null);
    }
  }, [documentQuery.data, parsedInvoiceId]);

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

  const lockedFieldsMutation = useMutation({
    mutationFn: async () => {
      if (!document || !isInvoiceIdValid) {
        return null;
      }

      let savedDocument: ApiAgentConsignmentDocumentDto | null = null;
      if (documentQuery.data && !areEditorDocumentsEqual(document, documentQuery.data)) {
        if (!await requestConfirmation({ title: "保存后查看锁定字段", description: "当前草稿有未保存修改，需要先保存后再读取锁定字段。", confirmLabel: "保存并继续" })) {
          return null;
        }

        const saveResponse = await client.saveAgentConsignmentDocument({
          invoiceId: parsedInvoiceId,
          body: normalizeAgentConsignmentDocumentForSave(document, parsedInvoiceId),
        });
        savedDocument = saveResponse.document;
      }

      const lockedFieldsResponse = await client.getAgentConsignmentLockedFields({ invoiceId: parsedInvoiceId });
      return { savedDocument, lockedFieldsResponse };
    },
    onSuccess: (result) => {
      if (!result) {
        return;
      }

      if (result.savedDocument) {
        setDocument(result.savedDocument);
        setPersistedDocumentSnapshot(buildAgentConsignmentDocumentSnapshot(result.savedDocument, parsedInvoiceId));
        setUndoDocument(null);
        queryClient.setQueryData(documentQueryKey, result.savedDocument);
        void Promise.all([
          queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() }),
          queryClient.invalidateQueries({ queryKey: reviewQueryKey }),
        ]);
      }

      setLockedFields(result.lockedFieldsResponse.fields);
      setSelectedLockedFieldKeys(new Set());
      setLockedFieldsOpen(true);
      setMessage(null);
      setSuccessMessage(null);
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const unlockFieldsMutation = useMutation({
    mutationFn: (fieldKeys: string[]) =>
      client.unlockAgentConsignmentFields({
        invoiceId: parsedInvoiceId,
        body: { fieldKeys },
      }),
    onSuccess: (response) => {
      setDocument(response.document);
      setPersistedDocumentSnapshot(buildAgentConsignmentDocumentSnapshot(response.document, parsedInvoiceId));
      setUndoDocument(null);
      setLockedFields(response.lockedFields);
      setSelectedLockedFieldKeys(new Set());
      queryClient.setQueryData(documentQueryKey, response.document);
      setMessage(null);
      setSuccessMessage(response.message || "字段已恢复为当前建议值。");
      void Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() }),
        queryClient.invalidateQueries({ queryKey: reviewQueryKey }),
      ]);
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const isBusy =
    documentQuery.isFetching ||
    reviewQuery.isFetching ||
    buildDefaultsMutation.isPending ||
    fillEmptyMutation.isPending ||
    scopedClearMutation.isPending ||
    repairReviewMutation.isPending ||
    saveMutation.isPending ||
    lockedFieldsMutation.isPending ||
    unlockFieldsMutation.isPending;
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

  function handleOpenLockedFields() {
    if (!document || !isInvoiceIdValid) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    lockedFieldsMutation.mutate();
  }

  function toggleLockedField(key: string) {
    setSelectedLockedFieldKeys((current) => {
      const next = new Set(current);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }

      return next;
    });
  }

  function toggleAllLockedFields() {
    setSelectedLockedFieldKeys((current) =>
      current.size === lockedFields.length ? new Set() : new Set(lockedFields.map((field) => field.key)),
    );
  }

  function handleUnlockSelectedFields() {
    const fieldKeys = Array.from(selectedLockedFieldKeys);
    if (fieldKeys.length === 0 || !isInvoiceIdValid) {
      return;
    }

    unlockFieldsMutation.mutate(fieldKeys);
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
        onOpenLockedFields={handleOpenLockedFields}
        onUndo={handleUndoToolAction}
        onBuildReview={() => void reviewQuery.refetch()}
      />

      {loadMessage || message ? <div className="alert">{loadMessage || message}</div> : null}
      {referenceMessage ? <div className="alert">报关代理委托候选项加载失败：{referenceMessage}</div> : null}
      {successMessage ? <div className="success-alert">{successMessage}</div> : null}
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
            {reviewMessage ? <div className="alert">{reviewMessage}</div> : null}
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

      {lockedFieldsOpen ? (
        <SingleWindowLockedFieldsDialog
          title="代理委托字段锁定"
          fields={lockedFields}
          selectedKeys={selectedLockedFieldKeys}
          isBusy={unlockFieldsMutation.isPending}
          onClose={() => setLockedFieldsOpen(false)}
          onToggleField={toggleLockedField}
          onToggleAll={toggleAllLockedFields}
          onUnlockSelected={handleUnlockSelectedFields}
        />
      ) : null}
    </section>
  );
}

function AgentConsignmentSummary({ document }: { document: ApiAgentConsignmentDocumentDto }) {
  return (
    <div className="detail-grid single-window-document-summary-grid">
      <SummaryItem label="发票 ID" value={document.sourceInvoiceId} />
      <SummaryItem label="发票号" value={document.invoiceNo} />
      <SummaryItem label="合同号" value={document.contractNo} />
      <SummaryItem label="状态" value={document.status} />
      <SummaryItem label="委托编号" value={document.consignNo} />
      <SummaryItem label="对方状态" value={document.counterpartyStatus} />
      <SummaryItem label="人工锁定字段" value={document.manualLockedFieldCount} />
      <SummaryItem label="来源差异" value={document.sourceDiffCount} />
      <SummaryItem label="预警" value={document.warningCount} />
      <SummaryItem label="最后生成" value={formatAgentDateTime(document.lastGeneratedAt)} />
      <SummaryItem label="来源差异摘要" value={document.sourceDiffSummary} wide />
      <SummaryItem label="预警摘要" value={document.warningSummary} wide />
    </div>
  );
}

function AgentConsignmentWorkbench({
  document,
  editorOptions,
  onPatchDocument,
}: {
  document: ApiAgentConsignmentDocumentDto;
  editorOptions: ReturnType<typeof buildAgentConsignmentEditorOptions>;
  onPatchDocument: (next: Partial<ApiAgentConsignmentDocumentDto>) => void;
}) {
  return (
    <div className="agent-consignment-workbench">
      <div className="agent-consignment-workbench-main">
        <AgentConsignmentCard
          title="OperInfo 操作信息"
          meta="企业编码、操作类型和签名会进入导入请求的操作段。"
        >
          <div className="field-grid agent-consignment-compact-grid">
            <TextField
              label="企业内部编号"
              value={document.copCusCode}
              required
              description="10 位企业海关编码，通常与经营单位编码一致。"
              onChange={(value) => onPatchDocument({ copCusCode: value })}
            />
            <SelectField
              label="操作类型"
              value={document.operType}
              options={agentOperTypeOptions}
              description="常规新增委托使用 1。"
              onChange={(value) => onPatchDocument({ operType: value })}
            />
            <TextField
              label="数字签名"
              value={document.sign}
              description="正式导入时由官方签名机制处理，可先留空。"
              onChange={(value) => onPatchDocument({ sign: value })}
            />
          </div>
        </AgentConsignmentCard>

        <AgentConsignmentCard
          title="ImportInfo 核心申报"
          meta="优先复核必填字段，决定交接包能否顺利导入。"
        >
          <div className="field-grid agent-consignment-critical-grid">
            <TextField
              label="主要货物名称"
              value={document.gName}
              required
              description="默认取首项商品中文品名，必要时可改为业务概括。"
              onChange={(value) => onPatchDocument({ gName: value })}
            />
            <TextField
              label="HS编码"
              value={document.codeTS}
              required
              description="10 位以内 HS 编码。"
              onChange={(value) => onPatchDocument({ codeTS: value })}
            />
            <TextField
              label="货物总价"
              value={document.declTotal}
              required
              description="最多 4 位小数。"
              onChange={(value) => onPatchDocument({ declTotal: value })}
            />
            <TextField
              label="进出口日期"
              value={document.ieDate}
              required
              description="格式 yyyyMMdd，例如 20260417。"
              onChange={(value) => onPatchDocument({ ieDate: value })}
            />
            <AgentCandidateField
              label="贸易方式"
              value={document.tradeMode}
              required
              options={editorOptions.tradeModeOptions}
              description="使用 ACD 监管方式代码，例如一般贸易 0110。"
              onChange={(value) => onPatchDocument({ tradeMode: value })}
            />
            <AgentCandidateField
              label="原产地/货源地"
              value={document.oriCountry}
              required
              options={editorOptions.countryOptions}
              description="使用海关 GBDQ 代码，例如中国 142。"
              onChange={(value) => onPatchDocument({ oriCountry: value })}
            />
            <TextField
              label="经营单位(委托方)海关10位编码"
              value={document.tradeCode}
              required
              onChange={(value) => onPatchDocument({ tradeCode: value })}
            />
            <TextField
              label="申报单位(被委托方)海关10位编码"
              value={document.agentCode}
              required
              onChange={(value) => onPatchDocument({ agentCode: value })}
            />
          </div>
        </AgentConsignmentCard>

        <AgentConsignmentCard title="辅助申报" meta="非必填但常用于现场导入和人工复核。">
          <div className="field-grid agent-consignment-compact-grid">
            <TextField label="提单号" value={document.listNo} onChange={(value) => onPatchDocument({ listNo: value })} />
            <AgentCandidateField
              label="币制代码"
              value={document.curr}
              options={editorOptions.currencyOptions}
              description="使用 ACD 海关币制码，例如 USD 为 502。"
              onChange={(value) => onPatchDocument({ curr: value })}
            />
            <TextField
              label="数量/重量"
              value={document.qtyOrWeight}
              onChange={(value) => onPatchDocument({ qtyOrWeight: value })}
            />
            <AgentCandidateField
              label="包装情况"
              value={document.packingCondition}
              options={agentPackingConditionOptions.map((value) => ({ value, label: value }))}
              onChange={(value) => onPatchDocument({ packingCondition: value })}
            />
            <TextAreaField
              label="其他要求"
              value={document.otherNote}
              className="agent-consignment-wide-text"
              onChange={(value) => onPatchDocument({ otherNote: value })}
            />
          </div>
        </AgentConsignmentCard>
      </div>
    </div>
  );
}

function AgentConsignmentDocumentsPanel({
  document,
  onPatchDocument,
}: {
  document: ApiAgentConsignmentDocumentDto;
  onPatchDocument: (next: Partial<ApiAgentConsignmentDocumentDto>) => void;
}) {
  return (
    <div className="agent-consignment-documents-grid">
      <AgentConsignmentCard title="联系与收件" meta="用于委托双方联系、单证交接和后续补充。">
        <div className="field-grid agent-consignment-compact-grid">
          <TextField
            label="委托方电话"
            value={document.consignTele}
            onChange={(value) => onPatchDocument({ consignTele: value })}
          />
          <TextField
            label="被委托方电话"
            value={document.declTele}
            onChange={(value) => onPatchDocument({ declTele: value })}
          />
          <TextField
            label="收到证件日期"
            value={document.receiveDate}
            description="格式 yyyyMMdd。"
            onChange={(value) => onPatchDocument({ receiveDate: value })}
          />
          <AgentCandidateField
            label="收到单证情况"
            value={document.paperInfo}
            options={agentPaperInfoOptions.map((value) => ({ value, label: value }))}
            onChange={(value) => onPatchDocument({ paperInfo: value })}
          />
          <TextAreaField
            label="其他收件信息"
            value={document.otherRecInfo}
            className="agent-consignment-wide-text"
            onChange={(value) => onPatchDocument({ otherRecInfo: value })}
          />
        </div>
      </AgentConsignmentCard>

      <AgentConsignmentCard title="单证与费用" meta="报关单号、收费和承诺说明可在确认后补录。">
        <div className="field-grid agent-consignment-compact-grid">
          <TextField
            label="报关单编号"
            value={document.entryId}
            onChange={(value) => onPatchDocument({ entryId: value })}
          />
          <TextField
            label="报关收费"
            value={document.declarePrice}
            description="人民币金额，最多 2 位小数。"
            onChange={(value) => onPatchDocument({ declarePrice: value })}
          />
          <TextAreaField
            label="承诺说明"
            value={document.promiseNote}
            className="agent-consignment-wide-text"
            onChange={(value) => onPatchDocument({ promiseNote: value })}
          />
        </div>
      </AgentConsignmentCard>
    </div>
  );
}

function AgentConsignmentReceiptPanel({ document }: { document: ApiAgentConsignmentDocumentDto }) {
  return (
    <div className="agent-consignment-receipt-grid">
      <div className="agent-consignment-receipt-card">
        <span>委托编号</span>
        <strong>{readAgentDisplayText(document.consignNo)}</strong>
      </div>
      <div className="agent-consignment-receipt-card">
        <span>对方状态</span>
        <strong>{readAgentDisplayText(document.counterpartyStatus)}</strong>
      </div>
    </div>
  );
}

function AgentConsignmentCard({
  title,
  meta,
  children,
}: {
  title: string;
  meta?: string;
  children: ReactNode;
}) {
  return (
    <section className="agent-consignment-card">
      <div className="agent-consignment-card-header">
        <h3>{title}</h3>
        {meta ? <span>{meta}</span> : null}
      </div>
      {children}
    </section>
  );
}

function SummaryItem({ label, value, wide }: { label: string; value?: string | number; wide?: boolean }) {
  const displayValue = readAgentDisplayValue(value);

  return (
    <div className={wide ? "detail-item detail-item-wide" : "detail-item"}>
      <span>{label}</span>
      <strong title={displayValue}>{displayValue}</strong>
    </div>
  );
}

function AgentCandidateField({
  label,
  value,
  required,
  disabled,
  description,
  options,
  onChange,
}: {
  label: string;
  value?: string;
  required?: boolean;
  disabled?: boolean;
  description?: string;
  options: Array<{ value: string; label: string }>;
  onChange: (value: string) => void;
}) {
  const listId = `agent-candidate-${useId().replace(/:/g, "-")}`;
  const normalizedOptions = options.filter((option) => option.value.trim());

  return (
    <FieldShell label={label} required={required} disabled={disabled} description={description}>
      {(descriptionId) => (
      <>
      <input
        list={normalizedOptions.length > 0 ? listId : undefined}
        value={value ?? ""}
        required={required}
        disabled={disabled}
        aria-describedby={descriptionId}
        onChange={(event) => onChange(event.target.value)}
      />
      {normalizedOptions.length > 0 ? (
        <datalist id={listId}>
          {normalizedOptions.map((option) => (
            <option key={option.value} value={option.value} label={option.label} />
          ))}
        </datalist>
      ) : null}
      </>
      )}
    </FieldShell>
  );
}
