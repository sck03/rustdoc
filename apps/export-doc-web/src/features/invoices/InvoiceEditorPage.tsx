import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, Edit3, Trash2 } from "lucide-react";
import { useLocation, useNavigate, useParams, useSearchParams } from "react-router-dom";
import type { ApiInvoiceDetailDto, ApiUnitDto, ExportDocManagerApiClient, HsCodeKnowledgeFeedbackInput } from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { useWorkspaceDeviceProfile } from "../../app/workspaceDevice.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { normalizeText, readApiError, readRouteSuccessMessage } from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { ServerDraftUpdateNotice, useServerDraftSync } from "../../ui/serverDraftSync.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { ConcurrencyConflictNotice, InlineNotice, PageState, PermissionNotice } from "../../ui/PageState.tsx";
import { WorkspaceDeviceNotice } from "../../ui/WorkspaceDeviceNotice.tsx";
import {
  hasCustomOptionValue,
} from "../custom-options/customOptionModel.ts";
import { InvoiceMarksAndItemsPanel } from "./InvoiceMarksAndItemsPanel.tsx";
import { InvoiceStatusReasonDialog } from "./InvoiceStatusReasonDialog.tsx";
import {
  canDeleteInvoiceStatus,
  canUnverifyInvoiceStatus,
  canTransitionInvoiceStatus,
  createEmptyInvoice,
  getCounterpartInvoiceType,
  getInvoiceStatusActionLabel,
  getNextInvoiceStatus,
  isInvoiceEditableStatus,
  normalizeInvoiceForSave,
  normalizeInvoiceStatus,
  normalizeInvoiceType,
  readRouteInvoiceDraft,
  readRouteInvoiceImportAction,
  type RouteInvoiceImportAction,
  uppercaseInvoiceEnglishText,
} from "./invoiceModel.ts";
import { InvoiceEditorFormShell } from "./InvoiceEditorFormShell.tsx";
import { InvoiceEditorDocumentSections } from "./InvoiceEditorDocumentSections.tsx";
import { areInvoiceDraftsEqual } from "./invoiceDraftEquality.ts";
import {
  buildInvoiceSnapshot,
  mergeRouteInvoiceImportDraft,
  readInvoiceItemBlankRowCount,
} from "./invoiceEditorHelpers.ts";
import { calculateInvoiceTotals } from "./invoiceItemsEditorModel.ts";
import { useInvoiceEditorReferenceData } from "./useInvoiceEditorReferenceData.ts";
import { useInvoiceItemsWorkspace } from "./useInvoiceItemsWorkspace.ts";
import { useInvoicePersistenceOperations } from "./useInvoicePersistenceOperations.ts";

export function InvoiceEditorPage({
  businessDate,
  client,
  mode,
}: {
  businessDate: string;
  client: ExportDocManagerApiClient;
  mode: "new" | "edit";
}) {
  const invoicePermission = useModulePermission("document.invoices");
  const masterDataPermission = useModulePermission("document.master-data");
  const singleWindowPermission = useModulePermission("document.single-window");
  const reportDesignPermission = useModulePermission("document.reports");
  const requestConfirmation = useConfirmation();
  const { invoiceId } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
  const workspaceDeviceProfile = useWorkspaceDeviceProfile();
  const workspaceDeviceMode = workspaceDeviceProfile.mode;
  const workspaceDeviceCapabilities = workspaceDeviceProfile.capabilities;
  const [pageBusinessDate] = useState(() => businessDate);
  const [initialNewRouteState] = useState(() => ({
    invoiceDraft: readRouteInvoiceDraft(location.state, pageBusinessDate),
    successMessage: readRouteSuccessMessage(location.state),
  }));
  const routeSuccessMessage = mode === "new" ? initialNewRouteState.successMessage : readRouteSuccessMessage(location.state);
  const routeInvoiceDraft = useMemo(
    () => (mode === "new" ? initialNewRouteState.invoiceDraft : readRouteInvoiceDraft(location.state, pageBusinessDate)),
    [initialNewRouteState.invoiceDraft, location.state, mode, pageBusinessDate],
  );
  const routeInvoiceImportAction = useMemo(() => readRouteInvoiceImportAction(location.state), [location.state]);
  const [invoice, setInvoice] = useState<ApiInvoiceDetailDto | null>(() =>
    mode === "new" ? routeInvoiceDraft ?? createEmptyInvoice(pageBusinessDate) : null,
  );
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(routeSuccessMessage);
  const [concurrencyMessage, setConcurrencyMessage] = useState<string | null>(null);
  const [isLetterOfCreditBusy, setIsLetterOfCreditBusy] = useState(false);
  const [persistedInvoiceStatus, setPersistedInvoiceStatus] = useState<string>(() =>
    mode === "new" ? normalizeInvoiceStatus(routeInvoiceDraft?.status) : "",
  );
  const [persistedInvoiceDraft, setPersistedInvoiceDraft] = useState<ApiInvoiceDetailDto | null>(null);
  const [pendingHsFeedback, setPendingHsFeedback] = useState<HsCodeKnowledgeFeedbackInput[]>([]);
  const [cancelReason, setCancelReason] = useState("");
  const [isCancelReasonDialogOpen, setIsCancelReasonDialogOpen] = useState(false);
  const [appliedRouteInvoiceImportKey, setAppliedRouteInvoiceImportKey] = useState<string | null>(null);

  const parsedInvoiceId = Number(invoiceId);
  const isNew = mode === "new";
  const isInvoiceItemsWorkbenchMode = searchParams.get("workbench") === "items"
    && workspaceDeviceCapabilities.canUseDenseWorkbench;
  const isInvoiceIdValid = Number.isInteger(parsedInvoiceId) && parsedInvoiceId > 0;
  const routeInvoiceImportKey =
    !isNew && routeInvoiceDraft && routeInvoiceImportAction
      ? `${parsedInvoiceId}:${routeInvoiceImportAction}:${routeInvoiceDraft.invoiceNo}:${routeInvoiceDraft.type}:${
          routeInvoiceDraft.items?.length ?? 0
        }`
      : null;
  const isInvoiceEditable = invoicePermission.canOperate
    && (isNew || isInvoiceEditableStatus(persistedInvoiceStatus || invoice?.status));
  const itemsWorkspace = useInvoiceItemsWorkspace({
    client,
    invoice,
    setInvoice,
    setSuccessMessage,
    isEditable: isInvoiceEditable && workspaceDeviceCapabilities.canUseDenseWorkbench,
    canSaveToProductLibrary: masterDataPermission.canOperate,
  });

  const invoiceQuery = useQuery({
    queryKey: queryKeys.invoice(parsedInvoiceId),
    queryFn: ({ signal }) => client.getInvoice({ id: parsedInvoiceId }, { signal }),
    enabled: !isNew && isInvoiceIdValid,
  });

  const statusHistoryQuery = useQuery({
    queryKey: queryKeys.invoiceStatusHistory(parsedInvoiceId),
    queryFn: ({ signal }) => client.listInvoiceStatusHistory({ id: parsedInvoiceId }, { signal }),
    enabled: !isNew && isInvoiceIdValid,
    staleTime: 30 * 1000,
  });

  const selectedCustomerId = invoice?.customerId ?? 0;
  const selectedExporterId = invoice?.exporterId ?? 0;
  const {
    selectedCustomerQuery,
    selectedExporterQuery,
    unitsQuery,
    settingsQuery,
    customOptionsQuery,
  } = useInvoiceEditorReferenceData(client, selectedCustomerId, selectedExporterId);

  const {
    cloneInvoiceTypeMutation,
    deleteInvoiceMutation,
    exporterSealMutation,
    refreshParties,
    saveCustomOptionMutation,
    saveInvoiceMutation,
    statusTransitionMutation,
    unverifyInvoiceMutation,
  } = useInvoicePersistenceOperations({
    client,
    invoice,
    invoiceId: parsedInvoiceId,
    isNew,
    refreshSelectedExporter: async () => selectedExporterQuery.refetch(),
    resetItemEditHistory: itemsWorkspace.resetEditHistory,
    setConcurrencyMessage,
    setInvoice,
    setMessage,
    setPendingHsFeedback,
    setPersistedInvoiceDraft,
    setPersistedInvoiceStatus,
    setSuccessMessage,
  });

  useEffect(() => {
    if (isNew) {
      const nextInvoice = routeInvoiceDraft ?? createEmptyInvoice(pageBusinessDate);
      setInvoice(nextInvoice);
      setPersistedInvoiceDraft(normalizeInvoiceForSave(nextInvoice, 0));
      setPersistedInvoiceStatus(normalizeInvoiceStatus(nextInvoice.status));
      setPendingHsFeedback([]);
      itemsWorkspace.reset();
      setMessage(null);
      setConcurrencyMessage(null);
      setSuccessMessage(routeSuccessMessage);
      return;
    }

    if (!isInvoiceIdValid) {
      setInvoice(null);
      setPersistedInvoiceDraft(null);
      setPersistedInvoiceStatus("");
      setPendingHsFeedback([]);
      itemsWorkspace.reset();
      setMessage("发票 ID 无效。");
      setSuccessMessage(null);
      return;
    }
  }, [isNew, isInvoiceIdValid, pageBusinessDate, parsedInvoiceId, routeInvoiceDraft, routeSuccessMessage]);

  useEffect(() => {
    if (!isNew && invoiceQuery.isError) {
      setMessage(readApiError(invoiceQuery.error));
      setSuccessMessage(null);
    }
  }, [invoiceQuery.error, invoiceQuery.isError, isNew]);


  const products = itemsWorkspace.products;
  const units: ApiUnitDto[] = unitsQuery.data ?? [];
  const invoiceCustomOptions = customOptionsQuery.data ?? {};
  const selectedCustomerEmail =
    invoice?.customerId && invoice.customerId > 0
      ? selectedCustomerQuery.data?.email ?? ""
      : "";
  const isBusy =
    invoiceQuery.isFetching ||
    saveInvoiceMutation.isPending ||
    cloneInvoiceTypeMutation.isPending ||
    statusTransitionMutation.isPending ||
    unverifyInvoiceMutation.isPending ||
    deleteInvoiceMutation.isPending ||
    exporterSealMutation.isPending ||
    isLetterOfCreditBusy;
  const isPartyBusy = selectedCustomerQuery.isFetching || selectedExporterQuery.isFetching || exporterSealMutation.isPending;
  const partyMessage = selectedCustomerQuery.isError
    ? readApiError(selectedCustomerQuery.error)
    : selectedExporterQuery.isError
      ? readApiError(selectedExporterQuery.error)
      : null;
  const productMessage = itemsWorkspace.productLibraryMessage;
  const unitLookupMessage = unitsQuery.isError ? readApiError(unitsQuery.error) : null;
  const isProductLibraryBusy = itemsWorkspace.isProductLibraryBusy;
  const invoiceItemBlankRowCount = readInvoiceItemBlankRowCount(settingsQuery.data?.settings);
  const targetInvoiceType = getCounterpartInvoiceType(invoice?.type);
  const cloneInvoiceTypeLabel = `生成${targetInvoiceType}`;
  const canUnverifyInvoice = !isNew && isInvoiceIdValid && canUnverifyInvoiceStatus(invoice?.status);
  const currentInvoiceDraft = useMemo(
    () => (invoice ? normalizeInvoiceForSave(invoice, isNew || !isInvoiceIdValid ? 0 : parsedInvoiceId, pendingHsFeedback) : undefined),
    [invoice, isInvoiceIdValid, isNew, parsedInvoiceId, pendingHsFeedback],
  );
  const hasUnsavedInvoiceChanges = Boolean(
    invoicePermission.canOperate &&
    invoice &&
    currentInvoiceDraft &&
    persistedInvoiceDraft &&
    !areInvoiceDraftsEqual(currentInvoiceDraft, persistedInvoiceDraft),
  );
  const serverDraftSync = useServerDraftSync({
    resourceKey: isNew ? "new" : parsedInvoiceId,
    incomingValue: isNew ? null : invoiceQuery.data,
    isDirty: hasUnsavedInvoiceChanges,
    fingerprint: (serverInvoice) => buildInvoiceSnapshot(serverInvoice, parsedInvoiceId),
    applyIncoming: (serverInvoice) => {
      let nextInvoice = serverInvoice;
      let appliedImportAction: RouteInvoiceImportAction | null = null;
      if (routeInvoiceDraft && routeInvoiceImportAction && routeInvoiceImportKey
        && appliedRouteInvoiceImportKey !== routeInvoiceImportKey) {
        nextInvoice = mergeRouteInvoiceImportDraft(
          serverInvoice,
          routeInvoiceDraft,
          routeInvoiceImportAction,
          parsedInvoiceId,
        );
        appliedImportAction = routeInvoiceImportAction;
      }

      setInvoice(nextInvoice);
      setPersistedInvoiceDraft(normalizeInvoiceForSave(serverInvoice, parsedInvoiceId));
      setPersistedInvoiceStatus(normalizeInvoiceStatus(serverInvoice.status));
      setPendingHsFeedback([]);
      itemsWorkspace.reset();
      setMessage(null);
      if (appliedImportAction && routeInvoiceImportKey) {
        setAppliedRouteInvoiceImportKey(routeInvoiceImportKey);
        setSuccessMessage(
          routeSuccessMessage ||
            (appliedImportAction === "AppendItems"
              ? "Excel 明细已追加到当前发票草稿，请核对后保存。"
              : "Excel 内容已覆盖当前发票草稿，请核对后保存。"),
        );
      } else if (routeSuccessMessage) {
        setSuccessMessage(routeSuccessMessage);
      }
    },
  });
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: hasUnsavedInvoiceChanges,
    message: "当前发票有未保存的修改。",
  });

  function patchInvoice(next: Partial<ApiInvoiceDetailDto>) {
    if (!isInvoiceEditable) {
      return;
    }

    setInvoice((current) => {
      if (!current) {
        return current;
      }

      const merged = { ...current, ...next };
      if ("exchangeRate" in next || "currency" in next) {
        return {
          ...merged,
          ...calculateInvoiceTotals(
            merged.items ?? [],
            merged.exchangeRate,
            merged.currency,
          ),
        };
      }

      return merged;
    });
    setSuccessMessage(null);
  }

  function handleHsKnowledgeFeedback(feedback: HsCodeKnowledgeFeedbackInput) {
    setPendingHsFeedback((current) => {
      const key = `${feedback.queryText.trim().toLowerCase()}|${feedback.candidateCode.trim()}|${feedback.productName.trim().toLowerCase()}|${feedback.specification.trim().toLowerCase()}`;
      const next = current.filter((item) =>
        `${item.queryText.trim().toLowerCase()}|${item.candidateCode.trim()}|${item.productName.trim().toLowerCase()}|${item.specification.trim().toLowerCase()}` !== key,
      );
      return [...next, feedback].slice(-100);
    });
    setSuccessMessage(null);
  }

  function uppercaseInvoiceText() {
    if (!isInvoiceEditable) {
      return;
    }

    setInvoice((current) => (current ? uppercaseInvoiceEnglishText(current) : current));
    setMessage(null);
    setSuccessMessage("英文名称、地址、运输条款和商品英文信息已统一转换为大写。");
  }

  function commitInvoiceCustomOption(optionType: string, value: string) {
    if (!invoicePermission.canOperate) return;

    const normalizedValue = normalizeText(value);
    if (!normalizedValue || hasCustomOptionValue(invoiceCustomOptions, optionType, normalizedValue)) {
      return;
    }

    saveCustomOptionMutation.mutate({ optionType, value: normalizedValue });
  }

  function clearInvoicePageMessages() {
    setMessage(null);
    setSuccessMessage(null);
  }

  function saveCurrentInvoiceDraft() {
    if (!invoice || !isInvoiceEditable || isBusy) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);

    const body = normalizeInvoiceForSave(invoice, isNew ? 0 : parsedInvoiceId, pendingHsFeedback);
    saveInvoiceMutation.mutate(body);
  }

  useEffect(() => {
    function handleDocumentKeyDown(event: globalThis.KeyboardEvent) {
      if (event.isComposing || !(event.ctrlKey || event.metaKey) || event.shiftKey || event.altKey || event.key.toLowerCase() !== "s") {
        return;
      }

      event.preventDefault();
      saveCurrentInvoiceDraft();
    }

    window.addEventListener("keydown", handleDocumentKeyDown);
    return () => window.removeEventListener("keydown", handleDocumentKeyDown);
  }, [invoice, isBusy, isInvoiceEditable, isNew, parsedInvoiceId, pendingHsFeedback]);

  async function handleCloneInvoiceType() {
    if (!invoicePermission.canOperate || !invoice || isNew || !isInvoiceIdValid) {
      return;
    }

    const sourceType = normalizeInvoiceType(invoice.type);
    const targetType = getCounterpartInvoiceType(invoice.type);
    if (!await confirmDiscardChanges(`从已保存的${sourceType}生成${targetType}`)) {
      return;
    }

    if (!await requestConfirmation({
      title: `生成${targetType}`,
      description: `将从已保存的${sourceType}生成同一发票号的${targetType}。`,
      details: ["目标口径已经存在时不会覆盖。", "当前发票的未保存修改不会带入。"],
      confirmLabel: `生成${targetType}`,
    })) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    cloneInvoiceTypeMutation.mutate({ targetType });
  }

  async function handleUnverifyInvoice() {
    if (!invoicePermission.canManage || !invoice || isNew || !isInvoiceIdValid || !canUnverifyInvoiceStatus(invoice.status)) {
      return;
    }

    const currentStatus = normalizeInvoiceStatus(invoice.status);
    if (!await requestConfirmation({
      title: "反审核发票",
      description: `当前状态“${currentStatus}”将退回草稿并允许继续编辑。`,
      details: ["反审核后请重新检查并保存修改。"],
      confirmLabel: "确认反审核",
      tone: "warning",
    })) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    unverifyInvoiceMutation.mutate();
  }

  async function handleTransitionInvoiceStatus(targetStatusOverride?: string, noteOverride?: string) {
    if (!invoicePermission.canOperate || !invoice || isNew || !isInvoiceIdValid || statusTransitionMutation.isPending) {
      return;
    }

    const targetStatus = getNextInvoiceStatus(invoice.status);
    const canCancel = invoicePermission.canManage && normalizeInvoiceStatus(invoice.status) !== "Cancelled";
    const requestedTarget = targetStatusOverride || targetStatus || (canCancel ? "Cancelled" : "");
    if (requestedTarget === "Cancelled" && !canCancel) {
      return;
    }
    if (!requestedTarget || !canTransitionInvoiceStatus(invoice.status, requestedTarget)) {
      return;
    }

    if (hasUnsavedInvoiceChanges) {
      setMessage("请先保存当前发票修改，再执行状态流转。状态操作只针对服务器上已保存的版本。");
      return;
    }

    const targetLabel = requestedTarget === "Verified" ? "已核对" : requestedTarget === "Shipped" ? "已出运" : requestedTarget === "Completed" ? "已结汇" : "已作废";
    let note = `用户确认状态变更为${targetLabel}。`;
    if (requestedTarget === "Cancelled") {
      const normalizedNote = noteOverride?.trim() ?? "";
      if (!normalizedNote) {
        setCancelReason("");
        setIsCancelReasonDialogOpen(true);
        return;
      }
      note = normalizedNote;
    }

    if (!await requestConfirmation({
      title: requestedTarget === "Cancelled" ? "作废发票" : getInvoiceStatusActionLabel(invoice.status),
      description: `将发票状态变更为“${targetLabel}”。`,
      details: ["状态变更会写入审计记录。", "如需继续编辑，锁定状态必须由管理人员反审核。"],
      confirmLabel: `确认${targetLabel}`,
      tone: requestedTarget === "Cancelled" ? "danger" : "warning",
    })) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    statusTransitionMutation.mutate({ targetStatus: requestedTarget, note });
  }

  function closeCancelReasonDialog() {
    if (!statusTransitionMutation.isPending) {
      setIsCancelReasonDialogOpen(false);
      setCancelReason("");
    }
  }

  function confirmCancelReasonDialog() {
    const note = cancelReason.trim();
    if (!note) {
      return;
    }

    setIsCancelReasonDialogOpen(false);
    setCancelReason("");
    void handleTransitionInvoiceStatus("Cancelled", note);
  }

  async function handleDeleteInvoice() {
    if (
      !invoicePermission.canManage
      || isNew
      || !isInvoiceIdValid
      || !invoice
      || !canDeleteInvoiceStatus(persistedInvoiceStatus || invoice.status)
      || deleteInvoiceMutation.isPending
    ) {
      return;
    }

    const title = invoice.invoiceNo?.trim() || invoice.customerNameEN?.trim() || `#${parsedInvoiceId}`;
    if (!await requestConfirmation({
      title: "删除发票",
      description: `确定删除当前草稿发票“${title}”吗？`,
      details: ["仅草稿允许直接删除，删除后无法恢复。", "已核对及后续状态只能作废；已作废记录默认保留审计。"],
      confirmLabel: "确认删除",
      tone: "danger",
    })) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    deleteInvoiceMutation.mutate();
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    saveCurrentInvoiceDraft();
  }

  async function handleBackToInvoiceList() {
    if (await confirmDiscardChanges("返回发票列表")) {
      navigate("/invoices");
    }
  }

  async function handleOpenCustomsCoo() {
    if (!singleWindowPermission.canView) return;

    if (await confirmDiscardChanges("打开海关原产地证编辑")) {
      navigate(`/single-window/coo/${parsedInvoiceId}`);
    }
  }

  async function handleOpenAgentConsignment() {
    if (!singleWindowPermission.canView) return;

    if (await confirmDiscardChanges("打开代理报关委托书编辑")) {
      navigate(`/single-window/acd/${parsedInvoiceId}`);
    }
  }

  async function handleReloadLatestInvoice() {
    if (!await requestConfirmation({
      title: "加载最新发票版本",
      description: "服务器上的发票已被其他用户修改。",
      details: ["当前页面尚未保存的修改将被替换。", "加载后请重新检查并继续编辑。"],
      confirmLabel: "加载最新版本",
    })) return;
    const result = await invoiceQuery.refetch();
    if (result.data) {
      setConcurrencyMessage(null);
      setMessage(null);
      setSuccessMessage("已加载服务器上的最新发票，请检查后继续编辑。");
    }
  }

  function scrollToInvoiceSection(sectionId: string) {
    document.getElementById(sectionId)?.scrollIntoView({
      behavior: "smooth",
      block: "start",
    });
  }

  function openInvoiceItemsWorkbench() {
    const nextSearchParams = new URLSearchParams(searchParams);
    nextSearchParams.set("workbench", "items");
    setSearchParams(nextSearchParams);
  }

  function closeInvoiceItemsWorkbench() {
    const nextSearchParams = new URLSearchParams(searchParams);
    nextSearchParams.delete("workbench");
    setSearchParams(nextSearchParams);
    window.requestAnimationFrame(() => scrollToInvoiceSection("invoice-items-section"));
  }

  const invoiceItemsPanel = invoice ? (
    <InvoiceMarksAndItemsPanel
      client={client}
      invoice={invoice}
      canSaveToProductLibrary={masterDataPermission.canOperate}
      canUseHsKnowledge={invoicePermission.canOperate && workspaceDeviceCapabilities.canUseDenseWorkbench}
      canRedoItemEdit={itemsWorkspace.canRedoItemEdit}
      canUndoItemEdit={itemsWorkspace.canUndoItemEdit}
      invoiceItemBlankRowCount={invoiceItemBlankRowCount}
      isEditable={isInvoiceEditable && workspaceDeviceCapabilities.canUseDenseWorkbench}
      isFocusedWorkbench={isInvoiceItemsWorkbenchMode}
      isProductLibraryBusy={isProductLibraryBusy}
      onChange={patchInvoice}
      onHsKnowledgeFeedback={handleHsKnowledgeFeedback}
      onAddItem={itemsWorkspace.addItem}
      onApplyProductLibraryItem={itemsWorkspace.applyProductLibraryItem}
      onChangeItem={itemsWorkspace.patchItem}
      onClearItemCells={itemsWorkspace.clearItemCells}
      onDuplicateItem={itemsWorkspace.duplicateItem}
      onFillDownItemCells={itemsWorkspace.fillDownItemCells}
      onFillDownItemField={itemsWorkspace.fillDownItemField}
      onMoveItem={itemsWorkspace.moveItem}
      onOpenFocusedWorkbench={workspaceDeviceCapabilities.canUseDenseWorkbench ? openInvoiceItemsWorkbench : undefined}
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
    <section className="editor-surface" aria-label={isNew ? "新建发票" : "编辑发票"}>
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
              data-state={saveInvoiceMutation.isPending ? "saving" : hasUnsavedInvoiceChanges ? "dirty" : "saved"}
              role="status"
              aria-live="polite"
            >
              {saveInvoiceMutation.isPending ? "保存中" : hasUnsavedInvoiceChanges ? "有未保存修改" : "已保存"}
            </span>
          ) : null}
        </div>
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
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      {!invoicePermission.canOperate ? (
        <PermissionNotice>
          当前权限模板仅允许查看发票；表头、商品明细、状态、信用证导入和保存操作已禁用。
        </PermissionNotice>
      ) : null}
      <WorkspaceDeviceNotice
        mode={workspaceDeviceMode}
        phone="手机端用于查看、搜索、审批和简单回填；商品明细工作台、批量录入、信用证处理和导入导出请使用桌面端。"
        tablet={workspaceDeviceCapabilities.canUseAdvancedTools
          ? "可进行轻量编辑、信用证处理和导入导出；商品明细密集工作台与批量录入仍需更宽屏幕。"
          : "平板端用于轻量编辑和现场确认；连接鼠标或触控板后可使用信用证处理和导入导出。"}
      />

      {!invoice && isBusy ? <PageState tone="loading" title="正在加载发票" description="请稍候，系统正在读取发票和商品明细。" /> : null}

      {invoice ? (
        <InvoiceEditorFormShell
          invoice={invoice}
          isWorkbench={isInvoiceItemsWorkbenchMode}
          isBusy={isBusy}
          isEditable={isInvoiceEditable}
          formClassName={isInvoiceItemsWorkbenchMode ? "invoice-form invoice-items-focus-form" : "invoice-form"}
          onSubmit={handleSubmit}
          onKeyDownCapture={handleEnterAsTabFormKeyDown}
          onCloseWorkbench={closeInvoiceItemsWorkbench}
          itemsPanel={invoiceItemsPanel}
          documentSections={
            <InvoiceEditorDocumentSections
              client={client}
              invoice={invoice}
              invoiceId={isNew ? 0 : parsedInvoiceId}
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
              canManageExporterSeals={masterDataPermission.canOperate}
              cloneInvoiceTypeBusy={cloneInvoiceTypeMutation.isPending}
              unverifyInvoiceBusy={unverifyInvoiceMutation.isPending}
              transitionStatusBusy={statusTransitionMutation.isPending}
              partyBusy={isPartyBusy}
              partyMessage={partyMessage}
              sealBusy={exporterSealMutation.isPending}
              profitAnalysisDisabled={!invoicePermission.canOperate || invoiceQuery.isFetching || saveInvoiceMutation.isPending}
              letterOfCreditDisabled={!isInvoiceEditable || !workspaceDeviceCapabilities.canUseAdvancedTools || !reportDesignPermission.canOperate || invoiceQuery.isFetching || saveInvoiceMutation.isPending}
              letterOfCreditReviewDisabled={!invoicePermission.canOperate || !reportDesignPermission.canOperate || invoiceQuery.isFetching || saveInvoiceMutation.isPending}
              onNavigate={scrollToInvoiceSection}
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
          }
        />
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
    </section>
  );
}
