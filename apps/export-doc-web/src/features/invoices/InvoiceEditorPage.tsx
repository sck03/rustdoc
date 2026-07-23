import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Edit3, Minimize2, PackageSearch, Save, Trash2 } from "lucide-react";
import { useLocation, useNavigate, useParams, useSearchParams } from "react-router-dom";
import type { ApiInvoiceDetailDto, ApiUnitDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { getWorkspaceDeviceCapabilities, useWorkspaceDeviceMode } from "../../app/workspaceDevice.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { isConcurrencyConflict, normalizeText, readApiError, readRouteSuccessMessage } from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { ConcurrencyConflictNotice, InlineNotice, PageState, PermissionNotice } from "../../ui/PageState.tsx";
import { WorkspaceDeviceNotice } from "../../ui/WorkspaceDeviceNotice.tsx";
import {
  hasCustomOptionValue,
  invoiceCustomOptionTypes,
  loadCustomOptionMap,
} from "../custom-options/customOptionModel.ts";
import {
  InvoiceBasicInfoPanel,
  InvoiceExtendedFieldsPanel,
  InvoiceMarksAndItemsPanel,
  InvoicePartiesPanel,
  InvoiceShippingTermsPanel,
} from "./InvoiceFormPanels.tsx";
import { InvoiceLetterOfCreditPanel } from "./InvoiceLetterOfCreditPanel.tsx";
import { InvoiceProfitAnalysisPanel } from "./InvoiceProfitAnalysisPanel.tsx";
import { InvoiceReportPreviewPanel } from "./InvoiceReportPreviewPanel.tsx";
import {
  canUnverifyInvoiceStatus,
  createEmptyInvoice,
  getCounterpartInvoiceType,
  isInvoiceEditableStatus,
  normalizeInvoiceForSave,
  normalizeInvoiceStatus,
  normalizeInvoiceType,
  readRouteInvoiceDraft,
  readRouteInvoiceImportAction,
  type RouteInvoiceImportAction,
  uppercaseInvoiceEnglishText,
} from "./invoiceModel.ts";
import { InvoiceEditorNavigation } from "./InvoiceEditorNavigation.tsx";
import {
  buildInvoiceSnapshot,
  mergeRouteInvoiceImportDraft,
  readInvoiceItemBlankRowCount,
} from "./invoiceEditorHelpers.ts";
import { useInvoiceItemsWorkspace } from "./useInvoiceItemsWorkspace.ts";

export function InvoiceEditorPage({
  client,
  mode,
}: {
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
  const workspaceDeviceMode = useWorkspaceDeviceMode();
  const workspaceDeviceCapabilities = getWorkspaceDeviceCapabilities(workspaceDeviceMode);
  const [initialNewRouteState] = useState(() => ({
    invoiceDraft: readRouteInvoiceDraft(location.state),
    successMessage: readRouteSuccessMessage(location.state),
  }));
  const routeSuccessMessage = mode === "new" ? initialNewRouteState.successMessage : readRouteSuccessMessage(location.state);
  const routeInvoiceDraft = useMemo(
    () => (mode === "new" ? initialNewRouteState.invoiceDraft : readRouteInvoiceDraft(location.state)),
    [initialNewRouteState.invoiceDraft, location.state, mode],
  );
  const routeInvoiceImportAction = useMemo(() => readRouteInvoiceImportAction(location.state), [location.state]);
  const [invoice, setInvoice] = useState<ApiInvoiceDetailDto | null>(() =>
    mode === "new" ? routeInvoiceDraft ?? createEmptyInvoice() : null,
  );
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(routeSuccessMessage);
  const [concurrencyMessage, setConcurrencyMessage] = useState<string | null>(null);
  const [isLetterOfCreditBusy, setIsLetterOfCreditBusy] = useState(false);
  const [persistedInvoiceStatus, setPersistedInvoiceStatus] = useState<string>(() =>
    mode === "new" ? normalizeInvoiceStatus(routeInvoiceDraft?.status) : "",
  );
  const [persistedInvoiceSnapshot, setPersistedInvoiceSnapshot] = useState<string | null>(null);
  const [appliedRouteInvoiceImportKey, setAppliedRouteInvoiceImportKey] = useState<string | null>(null);

  const parsedInvoiceId = Number(invoiceId);
  const isNew = mode === "new";
  const isInvoiceItemsWorkbenchMode = searchParams.get("workbench") === "items"
    && workspaceDeviceCapabilities.canUseDenseWorkbench;
  const isInvoiceIdValid = Number.isInteger(parsedInvoiceId) && parsedInvoiceId > 0;
  const queryClient = useQueryClient();
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
    queryFn: () => client.getInvoice({ id: parsedInvoiceId }),
    enabled: !isNew && isInvoiceIdValid,
  });

  const partiesQuery = useQuery({
    queryKey: queryKeys.invoiceParties(),
    queryFn: async () => {
      const [nextCustomers, nextExporters] = await Promise.all([
        client.listCustomers({}),
        client.listExporters({}),
      ]);
      return {
        customers: nextCustomers,
        exporters: nextExporters,
      };
    },
    staleTime: 5 * 60 * 1000,
  });

  const unitsQuery = useQuery({
    queryKey: queryKeys.masterDataRoot("units"),
    queryFn: () => client.listUnits({}),
    staleTime: 5 * 60 * 1000,
  });

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: () => client.getSettings(),
    staleTime: 5 * 60 * 1000,
  });

  const customOptionsQuery = useQuery({
    queryKey: queryKeys.customOptionsGroup("invoice-editor"),
    queryFn: () => loadCustomOptionMap(client, invoiceCustomOptionTypes),
    staleTime: 5 * 60 * 1000,
  });

  useEffect(() => {
    if (isNew) {
      const nextInvoice = routeInvoiceDraft ?? createEmptyInvoice();
      setInvoice(nextInvoice);
      setPersistedInvoiceSnapshot(buildInvoiceSnapshot(nextInvoice, 0));
      setPersistedInvoiceStatus(normalizeInvoiceStatus(nextInvoice.status));
      itemsWorkspace.reset();
      setMessage(null);
      setConcurrencyMessage(null);
      setSuccessMessage(routeSuccessMessage);
      return;
    }

    if (!isInvoiceIdValid) {
      setInvoice(null);
      setPersistedInvoiceSnapshot(null);
      setPersistedInvoiceStatus("");
      itemsWorkspace.reset();
      setMessage("发票 ID 无效。");
      setSuccessMessage(null);
      return;
    }
  }, [isNew, isInvoiceIdValid, parsedInvoiceId, routeInvoiceDraft, routeSuccessMessage]);

  useEffect(() => {
    if (!isNew && invoiceQuery.data) {
      if (routeInvoiceImportKey && appliedRouteInvoiceImportKey === routeInvoiceImportKey) {
        return;
      }

      let nextInvoice = invoiceQuery.data;
      let appliedImportAction: RouteInvoiceImportAction | null = null;
      if (routeInvoiceDraft && routeInvoiceImportAction && routeInvoiceImportKey) {
        nextInvoice = mergeRouteInvoiceImportDraft(invoiceQuery.data, routeInvoiceDraft, routeInvoiceImportAction, parsedInvoiceId);
        appliedImportAction = routeInvoiceImportAction;
      }

      setInvoice(nextInvoice);
      setPersistedInvoiceSnapshot(buildInvoiceSnapshot(invoiceQuery.data, parsedInvoiceId));
      setPersistedInvoiceStatus(normalizeInvoiceStatus(invoiceQuery.data.status));
      itemsWorkspace.reset();
      setMessage(null);
      if (appliedImportAction && routeInvoiceImportKey) {
        setAppliedRouteInvoiceImportKey(routeInvoiceImportKey);
        setSuccessMessage(
          routeSuccessMessage ||
            (appliedImportAction === "AppendItems" ? "Excel 明细已追加到当前发票草稿，请核对后保存。" : "Excel 内容已覆盖当前发票草稿，请核对后保存。"),
        );
      } else if (routeSuccessMessage) {
        setSuccessMessage(routeSuccessMessage);
      }
    }
  }, [
    appliedRouteInvoiceImportKey,
    invoiceQuery.data,
    isNew,
    parsedInvoiceId,
    routeInvoiceDraft,
    routeInvoiceImportAction,
    routeInvoiceImportKey,
    routeSuccessMessage,
  ]);

  useEffect(() => {
    if (!isNew && invoiceQuery.isError) {
      setMessage(readApiError(invoiceQuery.error));
      setSuccessMessage(null);
    }
  }, [invoiceQuery.error, invoiceQuery.isError, isNew]);

  const saveInvoiceMutation = useMutation({
    mutationFn: (body: ApiInvoiceDetailDto) =>
      isNew
        ? client.createInvoice({ body })
        : client.updateInvoice({ id: parsedInvoiceId, body }),
    onSuccess: async (response) => {
      setInvoice(response.invoice);
      setPersistedInvoiceSnapshot(buildInvoiceSnapshot(response.invoice, response.id));
      setPersistedInvoiceStatus(normalizeInvoiceStatus(response.invoice.status));
      itemsWorkspace.resetEditHistory();
      setMessage(null);
      setSuccessMessage(response.isUpdate ? "发票已保存。" : "发票已创建。");
      queryClient.setQueryData(queryKeys.invoice(response.id), response.invoice);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.invoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.invoiceParties() }),
      ]);
      if (isNew) {
        navigate(`/invoices/${response.id}`, {
          replace: true,
          state: { successMessage: "发票已创建。" },
        });
      }
    },
    onError: (error) => {
      const nextMessage = readApiError(error);
      setMessage(isConcurrencyConflict(error) ? null : nextMessage);
      setConcurrencyMessage(isConcurrencyConflict(error) ? nextMessage : null);
      setSuccessMessage(null);
    },
  });

  const cloneInvoiceTypeMutation = useMutation({
    mutationFn: ({ targetType }: { targetType: string }) =>
      client.cloneInvoiceAsType({
        id: parsedInvoiceId,
        body: {
          targetType,
          options: {
            copyHeader: true,
            copyItems: true,
            resetDates: false,
            resetStatus: true,
            clearAmounts: false,
          },
        },
      }),
    onSuccess: async (response) => {
      setMessage(null);
      setSuccessMessage(null);
      queryClient.setQueryData(queryKeys.invoice(response.id), response.invoice);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.invoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.queryInvoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard() }),
      ]);
      navigate(`/invoices/${response.id}`, {
        state: { successMessage: response.message || `已生成同一发票号的${response.invoice.type}。` },
      });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const unverifyInvoiceMutation = useMutation({
    mutationFn: () => client.unverifyInvoice({ id: parsedInvoiceId }),
    onSuccess: async (response) => {
      setInvoice(response.invoice);
      setPersistedInvoiceSnapshot(buildInvoiceSnapshot(response.invoice, response.id));
      setPersistedInvoiceStatus(normalizeInvoiceStatus(response.invoice.status));
      setMessage(null);
      setSuccessMessage("发票已反审核，当前为草稿状态。");
      queryClient.setQueryData(queryKeys.invoice(response.id), response.invoice);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.invoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.queryInvoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard() }),
      ]);
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const deleteInvoiceMutation = useMutation({
    mutationFn: () => client.deleteInvoice({ id: parsedInvoiceId }),
    onSuccess: async (response) => {
      setMessage(null);
      setSuccessMessage(null);
      queryClient.removeQueries({ queryKey: queryKeys.invoice(parsedInvoiceId) });
      queryClient.removeQueries({ queryKey: queryKeys.singleWindowCustomsCooDocument(parsedInvoiceId) });
      queryClient.removeQueries({ queryKey: queryKeys.singleWindowCustomsCooExportReview(parsedInvoiceId) });
      queryClient.removeQueries({ queryKey: queryKeys.singleWindowAgentConsignmentDocument(parsedInvoiceId) });
      queryClient.removeQueries({ queryKey: queryKeys.singleWindowAgentConsignmentExportReview(parsedInvoiceId) });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.invoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.queryInvoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() }),
      ]);
      navigate("/invoices", {
        replace: true,
        state: { successMessage: response.message || "发票已删除。" },
      });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const saveCustomOptionMutation = useMutation({
    mutationFn: ({ optionType, value }: { optionType: string; value: string }) =>
      client.saveCustomOption({
        optionType,
        body: { value },
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.customOptionsRoot() });
    },
  });

  const customers = partiesQuery.data?.customers ?? [];
  const exporters = partiesQuery.data?.exporters ?? [];
  const products = itemsWorkspace.products;
  const units: ApiUnitDto[] = unitsQuery.data ?? [];
  const invoiceCustomOptions = customOptionsQuery.data ?? {};
  const selectedCustomerEmail =
    invoice?.customerId && invoice.customerId > 0
      ? customers.find((customer) => customer.id === invoice.customerId)?.email ?? ""
      : "";
  const isBusy =
    invoiceQuery.isFetching ||
    saveInvoiceMutation.isPending ||
    cloneInvoiceTypeMutation.isPending ||
    unverifyInvoiceMutation.isPending ||
    deleteInvoiceMutation.isPending ||
    isLetterOfCreditBusy;
  const isPartyBusy = partiesQuery.isFetching;
  const partyMessage = partiesQuery.isError ? readApiError(partiesQuery.error) : null;
  const productMessage = itemsWorkspace.productLibraryMessage;
  const unitLookupMessage = unitsQuery.isError ? readApiError(unitsQuery.error) : null;
  const isProductLibraryBusy = itemsWorkspace.isProductLibraryBusy;
  const invoiceItemBlankRowCount = readInvoiceItemBlankRowCount(settingsQuery.data?.settings);
  const targetInvoiceType = getCounterpartInvoiceType(invoice?.type);
  const cloneInvoiceTypeLabel = `生成${targetInvoiceType}`;
  const canUnverifyInvoice = !isNew && isInvoiceIdValid && canUnverifyInvoiceStatus(invoice?.status);
  const currentInvoiceDraft = useMemo(
    () => (invoice ? normalizeInvoiceForSave(invoice, isNew || !isInvoiceIdValid ? 0 : parsedInvoiceId) : undefined),
    [invoice, isInvoiceIdValid, isNew, parsedInvoiceId],
  );
  const currentInvoiceSnapshot = useMemo(
    () => (currentInvoiceDraft ? JSON.stringify(currentInvoiceDraft) : null),
    [currentInvoiceDraft],
  );
  const hasUnsavedInvoiceChanges = Boolean(
    invoicePermission.canOperate &&
    invoice &&
      persistedInvoiceSnapshot &&
      currentInvoiceSnapshot &&
      currentInvoiceSnapshot !== persistedInvoiceSnapshot,
  );
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: hasUnsavedInvoiceChanges,
    message: "当前发票有未保存的修改。",
  });

  function loadParties() {
    void partiesQuery.refetch();
  }

  function patchInvoice(next: Partial<ApiInvoiceDetailDto>) {
    if (!isInvoiceEditable) {
      return;
    }

    setInvoice((current) => (current ? { ...current, ...next } : current));
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

    const body = normalizeInvoiceForSave(invoice, isNew ? 0 : parsedInvoiceId);
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
  }, [invoice, isBusy, isInvoiceEditable, isNew, parsedInvoiceId]);

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
    if (!invoicePermission.canOperate || !invoice || isNew || !isInvoiceIdValid || !canUnverifyInvoiceStatus(invoice.status)) {
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

  async function handleDeleteInvoice() {
    if (!invoicePermission.canManage || isNew || !isInvoiceIdValid || !invoice || deleteInvoiceMutation.isPending) {
      return;
    }

    const title = invoice.invoiceNo?.trim() || invoice.customerNameEN?.trim() || `#${parsedInvoiceId}`;
    if (!await requestConfirmation({
      title: "删除发票",
      description: `确定删除当前发票“${title}”吗？`,
      details: ["删除后无法在发票列表中继续查看。", "如有关联业务数据，服务端会拒绝删除并说明原因。"],
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
        {!isNew && isInvoiceIdValid && invoicePermission.canManage ? (
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
        tablet="平板端用于轻量编辑和现场确认；商品明细工作台、批量录入、信用证处理和导入导出请使用桌面端。"
      />

      {!invoice && isBusy ? <PageState tone="loading" title="正在加载发票" description="请稍候，系统正在读取发票和商品明细。" /> : null}

      {invoice ? (
        <form
          className={isInvoiceItemsWorkbenchMode ? "invoice-form invoice-items-focus-form" : "invoice-form"}
          onSubmit={handleSubmit}
          onKeyDownCapture={handleEnterAsTabFormKeyDown}
        >
          {isInvoiceItemsWorkbenchMode ? (
            <div className="invoice-items-focus-shell" aria-label="商品明细工作台">
              <div className="invoice-items-focus-header">
                <button className="command-button secondary" type="button" onClick={closeInvoiceItemsWorkbench}>
                  <Minimize2 size={17} aria-hidden="true" />
                  <span>返回发票</span>
                </button>
                <div className="invoice-items-focus-title">
                  <PackageSearch size={18} aria-hidden="true" />
                  <strong>商品明细工作台</strong>
                  <span>{invoice.invoiceNo || "新建发票"}</span>
                </div>
                <button className="command-button" type="submit" disabled={isBusy || !isInvoiceEditable}>
                  <Save size={17} aria-hidden="true" />
                  <span>保存</span>
                </button>
              </div>
              {invoiceItemsPanel}
            </div>
          ) : (
            <>
              <InvoiceEditorNavigation
                invoiceNo={invoice.invoiceNo || ""}
                editable={isInvoiceEditable}
                busy={isBusy}
                saving={saveInvoiceMutation.isPending}
                hasUnsavedChanges={hasUnsavedInvoiceChanges}
                onNavigate={scrollToInvoiceSection}
                onUppercase={uppercaseInvoiceText}
              />

              <div id="invoice-header-section" className="invoice-editor-section-anchor">
                <InvoiceBasicInfoPanel
                  invoice={invoice}
                  canOpenSingleWindowDocuments={!isNew && isInvoiceIdValid && singleWindowPermission.canOperate}
                  canCloneInvoiceType={!isNew && isInvoiceIdValid && invoicePermission.canOperate}
                  cloneInvoiceTypeLabel={cloneInvoiceTypeLabel}
                  canUnverifyInvoice={invoicePermission.canOperate && canUnverifyInvoice}
                  isEditable={isInvoiceEditable}
                  isBusy={isBusy}
                  isCloneInvoiceTypeBusy={cloneInvoiceTypeMutation.isPending}
                  isUnverifyInvoiceBusy={unverifyInvoiceMutation.isPending}
                  onChange={patchInvoice}
                  onCloneInvoiceType={handleCloneInvoiceType}
                  onUnverifyInvoice={handleUnverifyInvoice}
                  onOpenCustomsCoo={handleOpenCustomsCoo}
                  onOpenAgentConsignment={handleOpenAgentConsignment}
                  customOptions={invoiceCustomOptions}
                  onCommitCustomOption={commitInvoiceCustomOption}
                />

                <InvoicePartiesPanel
                  invoice={invoice}
                  customers={customers}
                  exporters={exporters}
                  isEditable={isInvoiceEditable}
                  isBusy={isPartyBusy}
                  message={partyMessage}
                  onRefresh={() => void loadParties()}
                  onChange={patchInvoice}
                />

                <InvoiceShippingTermsPanel
                  invoice={invoice}
                  isNewInvoice={isNew}
                  isEditable={isInvoiceEditable}
                  customOptions={invoiceCustomOptions}
                  onChange={patchInvoice}
                  onCommitCustomOption={commitInvoiceCustomOption}
                />

                <details className="invoice-new-optional-section information-tier-advanced">
                  <summary>
                    <span>报关与扩展字段（低频）</span>
                    <small>报关行、备用字段和高级 JSON，按需展开</small>
                  </summary>
                  <InvoiceExtendedFieldsPanel
                    invoice={invoice}
                    isEditable={isInvoiceEditable && workspaceDeviceCapabilities.canUseAdvancedTools}
                    onChange={patchInvoice}
                  />
                </details>
              </div>

              <div id="invoice-items-section" className="invoice-editor-section-anchor">
                {invoiceItemsPanel}
              </div>

              <div id="invoice-analysis-section" className="invoice-editor-section-anchor">
                <InvoiceProfitAnalysisPanel
                  client={client}
                  invoice={invoice}
                  invoiceId={isNew ? 0 : parsedInvoiceId}
                  disabled={!invoicePermission.canOperate || invoiceQuery.isFetching || saveInvoiceMutation.isPending}
                />

                <InvoiceLetterOfCreditPanel
                  client={client}
                  invoice={invoice}
                  disabled={!isInvoiceEditable || !workspaceDeviceCapabilities.canUseAdvancedTools || !reportDesignPermission.canOperate || invoiceQuery.isFetching || saveInvoiceMutation.isPending}
                  reviewDisabled={!invoicePermission.canOperate || !reportDesignPermission.canOperate || invoiceQuery.isFetching || saveInvoiceMutation.isPending}
                  onChange={patchInvoice}
                  onClearPageMessages={clearInvoicePageMessages}
                  onBusyChange={setIsLetterOfCreditBusy}
                />
              </div>

              <div id="invoice-report-section" className="invoice-editor-section-anchor">
                <InvoiceReportPreviewPanel
                  client={client}
                  invoiceId={isNew || !isInvoiceIdValid ? 0 : parsedInvoiceId}
                  invoiceDraft={currentInvoiceDraft}
                  invoiceNo={invoice.invoiceNo}
                  customerName={invoice.customerNameEN}
                  defaultToAddress={selectedCustomerEmail}
                  hasUnsavedDraftChanges={hasUnsavedInvoiceChanges}
                />
              </div>
            </>
          )}
        </form>
      ) : null}
    </section>
  );
}
