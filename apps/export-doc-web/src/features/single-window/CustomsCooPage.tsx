import { EditorWorkspace } from "../../ui/layout/Workspaces.tsx";
import { FormEvent, useMemo, useState } from "react";
import "../../styles/routes/single-window.css";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { FileCheck2 } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import {
  ApiCustomsCooDocumentDto,
  ApiCustomsCooEditorOptionsResponse,
  ApiCustomsCooOptionDto,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { queryKeys } from "../../api/queryKeys.ts";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { ServerDraftUpdateNotice, useServerDraftSync } from "../../ui/serverDraftSync.tsx";
import { InlineNotice, PageState, PermissionNotice } from "../../ui/PageState.tsx";
import { CustomsCooProducerProfileDialog } from "./CustomsCooProducerProfileDialog.tsx";
import { CustomsCooIdentitySections } from "./CustomsCooIdentitySections.tsx";
import { CustomsCooTradeSections } from "./CustomsCooTradeSections.tsx";
import { CustomsCooGoodsWorkspace } from "./CustomsCooGoodsWorkspace.tsx";
import { CooSummary, buildCustomsCooSectionNavItems } from "./CustomsCooSummary.tsx";
import { SingleWindowHandoffPanel } from "./SingleWindowHandoffPanel.tsx";
import { SingleWindowLockedFieldsDialog } from "./SingleWindowLockedFieldsDialog.tsx";
import { SingleWindowExportReviewPanel } from "./SingleWindowExportReviewPanel.tsx";
import { SingleWindowScopedClearControls } from "./SingleWindowScopedClearControls.tsx";
import { SingleWindowDocumentActionBar } from "./SingleWindowDocumentActionBar.tsx";
import { SingleWindowSectionNav } from "./SingleWindowSectionNav.tsx";
import { SingleWindowTabs } from "./SingleWindowNavigation.tsx";
import { useSingleWindowDocumentWorkspace } from "./useSingleWindowDocumentWorkspace.ts";
import { useSingleWindowLockedFields } from "./useSingleWindowLockedFields.ts";
import { useCustomsCooProducerProfiles } from "./useCustomsCooProducerProfiles.ts";
import { useCustomsCooAuthoritySelection } from "./useCustomsCooAuthoritySelection.ts";
import { useCustomsCooDocumentEditor } from "./useCustomsCooDocumentEditor.ts";
import {
  applyCooDefaultsForScope,
  applyCooDefaultsToEmptyFields,
  areEditorDocumentsEqual,
  clearCooManualOverrides,
  cooScopedClearOptionsByGroup,
} from "./singleWindowEditorTools.ts";
import {
  buildCooDocumentSnapshot,
  formatScopedClearResultMessage,
  normalizeCooDocumentForSave,
  toIssuingAuthorityOptions,
} from "./customsCooModel.ts";

const customsCooBusinessType = "CustomsCoo";
const cooScopedClearGroups = [
  { key: "证书基础", label: "证书基础" },
  { key: "申报与对象", label: "申报与对象" },
  { key: "运输与贸易", label: "运输与贸易" },
  { key: "补充与特殊项", label: "补充与特殊项" },
  { key: "更改与重发", label: "更改与重发" },
  { key: "商品明细", label: "商品明细" },
  { key: "附件", label: "附件" },
] as const;

const emptyCooOptionList: ApiCustomsCooOptionDto[] = [];
const emptyCooEditorOptions: ApiCustomsCooEditorOptionsResponse = {
  applyTypeOptions: emptyCooOptionList,
  certStatusOptions: emptyCooOptionList,
  certTypeOptions: emptyCooOptionList,
  producerSecretOptions: emptyCooOptionList,
  exhibitFlagOptions: emptyCooOptionList,
  thirdPartyInvoiceOptions: emptyCooOptionList,
  predictFlagOptions: emptyCooOptionList,
  promiseOptions: emptyCooOptionList,
  currencyOptions: emptyCooOptionList,
  cooTradeModeOptions: emptyCooOptionList,
  goodsItemFlagOptions: emptyCooOptionList,
  packTypeOptions: emptyCooOptionList,
  goodsTaxRateOptions: emptyCooOptionList,
  packUnitOptions: emptyCooOptionList,
  originCriteriaOptionSets: [],
  originCriteriaSubOptionSets: [],
  storagePolicy: "",
};

export function CustomsCooPage({ client }: { client: ExportDocManagerApiClient }) {
  const permission = useModulePermission("document.single-window");
  const { sourceId } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const parsedInvoiceId = Number(sourceId);
  const isInvoiceIdValid = Number.isInteger(parsedInvoiceId) && parsedInvoiceId > 0;
  const documentQueryKey = queryKeys.singleWindowCustomsCooDocument(parsedInvoiceId);
  const reviewQueryKey = queryKeys.singleWindowCustomsCooExportReview(parsedInvoiceId);

  const [document, setDocument] = useState<ApiCustomsCooDocumentDto | null>(null);
  const [undoDocument, setUndoDocument] = useState<ApiCustomsCooDocumentDto | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [persistedDocumentSnapshot, setPersistedDocumentSnapshot] = useState<string | null>(null);
  const {
    addAttachmentsFromDialog,
    addItem,
    addNonpartyCorp,
    copyOriginAndEnterpriseToFollowingRows: handleCopyOriginAndEnterpriseToFollowingRows,
    generateGoodsDescription: handleGenerateGoodsDescription,
    patchAttachment,
    patchDocument,
    patchItem,
    patchNonpartyCorp,
    removeAttachment,
    removeItem,
    removeNonpartyCorp,
    undoToolAction: handleUndoToolAction,
  } = useCustomsCooDocumentEditor({
    scope: `${parsedInvoiceId}:${permission.canOperate}`, owner: client,
    document,
    undoDocument,
    setDocument,
    setUndoDocument,
    setMessage,
    setSuccessMessage,
  });

  const documentQuery = useQuery({
    queryKey: documentQueryKey,
    queryFn: ({ signal }) => client.getCustomsCooDocument({ sourceId: parsedInvoiceId }, { signal }),
    enabled: isInvoiceIdValid,
  });

  const editorOptionsQuery = useQuery({
    queryKey: queryKeys.singleWindowCustomsCooEditorOptions(),
    queryFn: ({ signal }) => client.getCustomsCooEditorOptions({ signal }),
    enabled: isInvoiceIdValid,
    staleTime: 10 * 60 * 1000,
  });

  const issuingAuthoritiesQuery = useQuery({
    queryKey: queryKeys.singleWindowCustomsCooIssuingAuthorities(),
    queryFn: ({ signal }) => client.getCustomsCooIssuingAuthorities({ signal }),
    enabled: isInvoiceIdValid,
    staleTime: 10 * 60 * 1000,
  });

  const reviewQuery = useQuery({
    queryKey: reviewQueryKey,
    queryFn: ({ signal }) =>
      client.getSingleWindowExportReview({
        businessType: customsCooBusinessType,
        sourceId: parsedInvoiceId,
      }, { signal }),
    enabled: isInvoiceIdValid,
  });

  const workspace = useSingleWindowDocumentWorkspace({
    scope: String(parsedInvoiceId), owner: client, enabled: isInvoiceIdValid && permission.canOperate,
    document, setDocument, setUndoDocument, setMessage, setSuccessMessage, setPersistedDocumentSnapshot,
    documentQueryKey, reviewQueryKey,
    snapshot: value => buildCooDocumentSnapshot(value, parsedInvoiceId),
    saveDocument: (value, signal) => client.saveCustomsCooDocument({ sourceId: parsedInvoiceId, body: normalizeCooDocumentForSave(value, parsedInvoiceId) }, { signal }),
    getDocument: signal => client.getCustomsCooDocument({ sourceId: parsedInvoiceId }, { signal }),
    buildDefaults: signal => client.buildCustomsCooDefaults({ sourceId: parsedInvoiceId }, { signal }),
    repair: (groupKeys, signal) => client.repairSingleWindowExportReviewGroups({ businessType: customsCooBusinessType, sourceId: parsedInvoiceId, body: { groupKeys } }, { signal }),
    fillEmpty: applyCooDefaultsToEmptyFields, clearScope: applyCooDefaultsForScope, clearManual: clearCooManualOverrides,
    clearMessage: formatScopedClearResultMessage,
  });
  const lockedFieldsWorkspace = useSingleWindowLockedFields({
    workspace, document,
    hasUnsavedChanges: Boolean(document && documentQuery.data && !areEditorDocumentsEqual(document, documentQuery.data)),
    loadLockedFields: signal => client.getCustomsCooLockedFields({ sourceId: parsedInvoiceId }, { signal }),
    unlockFields: (fieldKeys, signal) => client.unlockCustomsCooFields({ sourceId: parsedInvoiceId, body: { fieldKeys } }, { signal }),
  });

  const producerProfiles = useCustomsCooProducerProfiles({ workspace, client, queryClient, document, setDocument: (next) => setDocument(next), setUndoDocument, setMessage, setSuccessMessage });

  const isBusy =
    documentQuery.isFetching ||
    reviewQuery.isFetching ||
    workspace.isPending ||
    producerProfiles.isPending;
  const loadMessage = !isInvoiceIdValid
    ? "申报资料 无效。"
    : documentQuery.isError
      ? readApiError(documentQuery.error)
      : null;
  const reviewMessage = reviewQuery.isError ? readApiError(reviewQuery.error) : null;
  const catalogMessage = editorOptionsQuery.isError
    ? readApiError(editorOptionsQuery.error)
    : issuingAuthoritiesQuery.isError
      ? readApiError(issuingAuthoritiesQuery.error)
      : null;
  const editorOptions = editorOptionsQuery.data ?? emptyCooEditorOptions;
  const issuingAuthorityOptions = issuingAuthoritiesQuery.data?.options ?? [];
  const authoritySelection = useCustomsCooAuthoritySelection(document, issuingAuthorityOptions, patchDocument);
  const issuingAuthorityCooOptions = useMemo(
    () => toIssuingAuthorityOptions(issuingAuthorityOptions),
    [issuingAuthorityOptions],
  );
  const currentDocumentSnapshot = useMemo(
    () => (document && isInvoiceIdValid ? buildCooDocumentSnapshot(document, parsedInvoiceId) : null),
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
    fingerprint: (serverDocument) => buildCooDocumentSnapshot(serverDocument, parsedInvoiceId),
    applyIncoming: (serverDocument) => {
      setDocument(serverDocument);
      setPersistedDocumentSnapshot(buildCooDocumentSnapshot(serverDocument, parsedInvoiceId));
      setUndoDocument(null);
      setMessage(null);
      authoritySelection.reset();
    },
  });
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: hasUnsavedDocumentChanges,
    message: "当前海关原产地证草稿有未保存的修改。",
  });

  const handleRepairReviewGroups = (groupKeys: string[]) => workspace.repair(groupKeys, hasUnsavedDocumentChanges);
  const handleRestoreDefaults = workspace.restore;
  const handleFillEmptyFields = workspace.fillEmpty;
  const handleClearManualOverrides = workspace.clearManual;
  const handleClearScopedGroup = workspace.clearScope;
  const handleClearScopedCategory = workspace.clearScope;

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
    <EditorWorkspace className="editor-surface customs-coo-surface" aria-label="海关原产地证草稿">
      <SingleWindowTabs activeKey="customs-coo" />

      <SingleWindowDocumentActionBar
        title={document ? document.invNo || document.invoiceNo || `发票 ${parsedInvoiceId}` : "海关原产地证草稿"}
        titleIcon={<FileCheck2 size={18} aria-hidden="true" />}
        formId="customs-coo-form"
        isBusy={isBusy}
        isDocumentReady={Boolean(document)}
        isInvoiceIdValid={isInvoiceIdValid}
        canOperate={permission.canOperate}
        canUndo={Boolean(undoDocument)}
        scopedClearControls={
          <SingleWindowScopedClearControls
            groups={cooScopedClearGroups}
            optionsByGroup={cooScopedClearOptionsByGroup}
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

      {loadMessage || message || catalogMessage ? <InlineNotice tone="error" title="原产地证操作未完成">{loadMessage || message || catalogMessage}</InlineNotice> : null}
      {serverDraftSync.hasPendingServerVersion ? <ServerDraftUpdateNotice
        entityLabel="海关原产地证草稿"
        onKeepLocal={serverDraftSync.keepLocalDraft}
        onLoadServer={serverDraftSync.loadServerVersion}
      /> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      {!permission.canOperate ? <PermissionNotice>当前权限模板仅允许查看单一窗口草稿和预检结果；修改、修复、保存与交接操作已禁用。</PermissionNotice> : null}
      {!document && isBusy ? <PageState tone="loading" title="正在加载原产地证草稿" description="正在读取表头、商品明细、附件和预检状态。" /> : null}

      {document ? (
        <form id="customs-coo-form" className="entity-form customs-coo-form" onSubmit={handleSubmit} onKeyDownCapture={handleEnterAsTabFormKeyDown}>
          <SingleWindowSectionNav
            items={buildCustomsCooSectionNavItems(document)}
            ariaLabel="海关原产地证录入分区"
          />

          <section id="coo-section-status" className="form-section single-window-editor-section" aria-label="草稿状态">
            <div className="section-header">
              <h2>草稿状态</h2>
              <span className="section-count">{document.items.length} 行商品</span>
            </div>
            <CooSummary document={document} />
          </section>

          <fieldset className="permission-fieldset" disabled={!permission.canOperate}>

          <CustomsCooIdentitySections
            document={document}
            editorOptions={editorOptions}
            issuingAuthorityOptions={issuingAuthorityCooOptions}
            onPatch={patchDocument}
            onOrgCodeChange={authoritySelection.changeOrgCode}
            onFetchPlaceChange={authoritySelection.changeFetchPlace}
            onApplicationAddressChange={authoritySelection.changeApplicationAddress}
          />

          <CustomsCooTradeSections document={document} editorOptions={editorOptions} onPatch={patchDocument} />

          <CustomsCooGoodsWorkspace
            document={document}
            editorOptions={editorOptions}
            disabled={isBusy}
            savingProducerRowIndex={producerProfiles.savingRowIndex}
            onAddItem={addItem}
            onChangeItem={patchItem}
            onRemoveItem={removeItem}
            onGenerateGoodsDescription={handleGenerateGoodsDescription}
            onCopyOriginAndEnterpriseToFollowingRows={handleCopyOriginAndEnterpriseToFollowingRows}
            onOpenProducerProfile={producerProfiles.open}
            onSaveProducerProfile={producerProfiles.save}
            onAddNonpartyCorp={addNonpartyCorp}
            onChangeNonpartyCorp={patchNonpartyCorp}
            onRemoveNonpartyCorp={removeNonpartyCorp}
            onSelectAttachments={() => void addAttachmentsFromDialog()}
            onChangeAttachment={patchAttachment}
            onRemoveAttachment={removeAttachment}
            onAttachmentPathError={(value) => {
              setMessage(value);
              setSuccessMessage(null);
            }}
          />
          </fieldset>

          <section id="coo-section-review" className="form-section single-window-editor-section" aria-label="导出前预检">
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

          <SingleWindowHandoffPanel businessType="CustomsCoo" client={client} sourceId={parsedInvoiceId} canOperate={permission.canOperate} dirty={hasUnsavedDocumentChanges} busy={isBusy} draftRevision={Number(document?.draftRevision ?? 0)} workspace={workspace} />
        </form>
      ) : null}

      {lockedFieldsWorkspace.isOpen ? (
        <SingleWindowLockedFieldsDialog
          title="海关原产地证字段锁定"
          fields={lockedFieldsWorkspace.fields}
          selectedKeys={lockedFieldsWorkspace.selectedFieldKeys}
          isBusy={lockedFieldsWorkspace.isPending}
          onClose={lockedFieldsWorkspace.close}
          onToggleField={lockedFieldsWorkspace.toggleField}
          onToggleAll={lockedFieldsWorkspace.toggleAll}
          onUnlockSelected={lockedFieldsWorkspace.unlockSelected}
        />
      ) : null}
      {producerProfiles.currentProfile ? (
        <CustomsCooProducerProfileDialog
          client={client}
          currentProfile={producerProfiles.currentProfile}
          rowLabel={producerProfiles.rowLabel}
          onApply={producerProfiles.apply}
          onClose={producerProfiles.close}
        />
      ) : null}
    </EditorWorkspace>
  );
}
