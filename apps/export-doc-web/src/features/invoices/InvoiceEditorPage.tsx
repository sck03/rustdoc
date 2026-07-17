import { FormEvent, useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, ClipboardList, Edit3, FileText, Minimize2, PackageSearch, Save, ScrollText, Trash2 } from "lucide-react";
import { useLocation, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { ApiInvoiceDetailDto, ApiInvoiceItemDto, ApiProductDto, ApiUnitDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { queryKeys } from "../../api/queryKeys.ts";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { normalizeText, readApiError, readNumber, readRouteSuccessMessage } from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
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
import {
  type InvoiceItemCellSelection,
  calculateInvoiceTotals,
  createEmptyInvoiceItem,
  recalculateInvoiceItem,
} from "./InvoiceItemsEditor.tsx";
import { type EditableInvoiceItemField, invoiceItemEditableColumns } from "./invoiceItemTableModel.ts";
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
} from "./invoiceModel.ts";
import { createInvoiceItemFromProduct, createProductDraftFromInvoiceItem, hasSameProductCode } from "./invoiceProductLibrary.ts";

const maxInvoiceItemHistoryDepth = 50;

type InvoiceItemEditHistory = {
  redo: ApiInvoiceItemDto[][];
  undo: ApiInvoiceItemDto[][];
};

const emptyInvoiceItemEditHistory: InvoiceItemEditHistory = {
  redo: [],
  undo: [],
};

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
  const { invoiceId } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
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
  const [isLetterOfCreditBusy, setIsLetterOfCreditBusy] = useState(false);
  const [itemEditHistory, setItemEditHistory] = useState<InvoiceItemEditHistory>(emptyInvoiceItemEditHistory);
  const [persistedInvoiceStatus, setPersistedInvoiceStatus] = useState<string>(() =>
    mode === "new" ? normalizeInvoiceStatus(routeInvoiceDraft?.status) : "",
  );
  const [productLibraryKeyword, setProductLibraryKeyword] = useState("");
  const [productLibraryMessage, setProductLibraryMessage] = useState<string | null>(null);
  const [persistedInvoiceSnapshot, setPersistedInvoiceSnapshot] = useState<string | null>(null);
  const [appliedRouteInvoiceImportKey, setAppliedRouteInvoiceImportKey] = useState<string | null>(null);

  const parsedInvoiceId = Number(invoiceId);
  const isNew = mode === "new";
  const isInvoiceItemsWorkbenchMode = searchParams.get("workbench") === "items";
  const isInvoiceIdValid = Number.isInteger(parsedInvoiceId) && parsedInvoiceId > 0;
  const queryClient = useQueryClient();
  const routeInvoiceImportKey =
    !isNew && routeInvoiceDraft && routeInvoiceImportAction
      ? `${parsedInvoiceId}:${routeInvoiceImportAction}:${routeInvoiceDraft.invoiceNo}:${routeInvoiceDraft.type}:${
          routeInvoiceDraft.items?.length ?? 0
        }`
      : null;

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

  const productsQuery = useQuery({
    queryKey: queryKeys.masterDataList("products", 1, 200, productLibraryKeyword),
    queryFn: () => client.listProducts({ keyword: productLibraryKeyword || undefined }),
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
      setItemEditHistory(emptyInvoiceItemEditHistory);
      setMessage(null);
      setProductLibraryMessage(null);
      setSuccessMessage(routeSuccessMessage);
      return;
    }

    if (!isInvoiceIdValid) {
      setInvoice(null);
      setPersistedInvoiceSnapshot(null);
      setPersistedInvoiceStatus("");
      setItemEditHistory(emptyInvoiceItemEditHistory);
      setMessage("发票 ID 无效。");
      setProductLibraryMessage(null);
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
      setItemEditHistory(emptyInvoiceItemEditHistory);
      setMessage(null);
      setProductLibraryMessage(null);
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
      setItemEditHistory(emptyInvoiceItemEditHistory);
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
      setMessage(readApiError(error));
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

  const saveProductMutation = useMutation({
    mutationFn: async ({ item }: { item: ApiInvoiceItemDto }) => {
      const productCode = normalizeText(item.styleNo);
      if (!productCode) {
        throw new Error("商品编码(款号)不能为空。");
      }

      const candidates = await client.listProducts({ keyword: productCode });
      const existing = candidates.find((product) => hasSameProductCode(product, productCode)) ?? null;
      if (existing && !window.confirm(`商品库中已存在编码为 ${productCode} 的商品，是否更新？`)) {
        return { cancelled: true, isUpdate: true, productCode };
      }

      const body = createProductDraftFromInvoiceItem(item, existing);
      if (existing) {
        await client.updateProduct({ id: existing.id, body });
        return { cancelled: false, isUpdate: true, productCode };
      }

      await client.createProduct({ body });
      return { cancelled: false, isUpdate: false, productCode };
    },
    onSuccess: async (result) => {
      if (result.cancelled) {
        setProductLibraryMessage("已取消商品库更新。");
        return;
      }

      setProductLibraryMessage(result.isUpdate ? `商品库已更新：${result.productCode}` : `商品已保存到商品库：${result.productCode}`);
      await queryClient.invalidateQueries({ queryKey: queryKeys.masterDataRoot("products") });
    },
    onError: (error) => {
      setProductLibraryMessage(readApiError(error));
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
  const products = productsQuery.data ?? [];
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
  const productMessage = productsQuery.isError ? readApiError(productsQuery.error) : productLibraryMessage;
  const unitLookupMessage = unitsQuery.isError ? readApiError(unitsQuery.error) : null;
  const isProductLibraryBusy = productsQuery.isFetching || saveProductMutation.isPending;
  const invoiceItemBlankRowCount = readInvoiceItemBlankRowCount(settingsQuery.data?.settings);
  const targetInvoiceType = getCounterpartInvoiceType(invoice?.type);
  const cloneInvoiceTypeLabel = `生成${targetInvoiceType}`;
  const canUnverifyInvoice = !isNew && isInvoiceIdValid && canUnverifyInvoiceStatus(invoice?.status);
  const isInvoiceEditable = invoicePermission.canOperate && (isNew || isInvoiceEditableStatus(persistedInvoiceStatus || invoice?.status));
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

  function searchProductLibrary(keyword: string) {
    const nextKeyword = normalizeText(keyword);
    setProductLibraryMessage(null);
    setProductLibraryKeyword((current) => {
      if (current === nextKeyword) {
        void productsQuery.refetch();
      }

      return nextKeyword;
    });
  }

  function refreshProductLibrary() {
    setProductLibraryMessage(null);
    void productsQuery.refetch();
  }

  function patchInvoice(next: Partial<ApiInvoiceDetailDto>) {
    if (!isInvoiceEditable) {
      return;
    }

    setInvoice((current) => (current ? { ...current, ...next } : current));
    setSuccessMessage(null);
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

  function handleCloneInvoiceType() {
    if (!invoicePermission.canOperate || !invoice || isNew || !isInvoiceIdValid) {
      return;
    }

    const sourceType = normalizeInvoiceType(invoice.type);
    const targetType = getCounterpartInvoiceType(invoice.type);
    if (!confirmDiscardChanges(`从已保存的${sourceType}生成${targetType}`)) {
      return;
    }

    if (
      !window.confirm(
        `将从已保存的${sourceType}生成同一发票号的${targetType}，目标口径已存在时不会覆盖。是否继续？`,
      )
    ) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    cloneInvoiceTypeMutation.mutate({ targetType });
  }

  function handleUnverifyInvoice() {
    if (!invoicePermission.canOperate || !invoice || isNew || !isInvoiceIdValid || !canUnverifyInvoiceStatus(invoice.status)) {
      return;
    }

    const currentStatus = normalizeInvoiceStatus(invoice.status);
    if (!window.confirm(`确定要反审核当前发票吗？当前状态 ${currentStatus} 将退回草稿并允许继续编辑。`)) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    unverifyInvoiceMutation.mutate();
  }

  function handleDeleteInvoice() {
    if (!invoicePermission.canManage || isNew || !isInvoiceIdValid || !invoice || deleteInvoiceMutation.isPending) {
      return;
    }

    const title = invoice.invoiceNo?.trim() || invoice.customerNameEN?.trim() || `#${parsedInvoiceId}`;
    if (!window.confirm(`确定删除当前发票 ${title} 吗？删除后无法在列表中继续查看。`)) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    deleteInvoiceMutation.mutate();
  }

  function setInvoiceItems(
    buildItems: (items: ApiInvoiceItemDto[], invoiceId: number) => ApiInvoiceItemDto[],
    options: { trackHistory?: boolean } = { trackHistory: true },
  ) {
    setInvoice((current) => {
      if (!isInvoiceEditable) {
        return current;
      }

      if (!current) {
        return current;
      }

      const currentItems = current.items ?? [];
      const nextItems = buildItems(currentItems, current.id);
      if (areInvoiceItemsEqual(currentItems, nextItems)) {
        return current;
      }

      if (options.trackHistory !== false) {
        const previousItems = cloneInvoiceItems(currentItems);
        setItemEditHistory((history) => ({
          undo: [...history.undo, previousItems].slice(-maxInvoiceItemHistoryDepth),
          redo: [],
        }));
      }

      return {
        ...current,
        items: nextItems,
        ...calculateInvoiceTotals(nextItems),
      };
    });
    setSuccessMessage(null);
  }

  function addInvoiceItem() {
    setInvoiceItems((items, invoiceId) => [...items, createEmptyInvoiceItem(invoiceId)]);
  }

  function applyProductLibraryItem(product: ApiProductDto, insertAfterIndex: number | null) {
    if (!product) {
      setProductLibraryMessage("请选择要套用的商品。");
      return;
    }

    setInvoiceItems((items, invoiceId) => {
      const targetIndex =
        insertAfterIndex == null || insertAfterIndex < 0 || insertAfterIndex >= items.length
          ? items.length
          : Math.min(items.length, insertAfterIndex + 1);
      const productItem = createInvoiceItemFromProduct(product, invoiceId);
      return [
        ...items.slice(0, targetIndex),
        productItem,
        ...items.slice(targetIndex),
      ];
    });
    setProductLibraryMessage(`已从商品库新增明细：${normalizeText(product.productCode) || product.id}`);
  }

  function saveInvoiceItemToProductLibrary(index: number) {
    if (!masterDataPermission.canOperate) {
      setProductLibraryMessage("当前权限只能读取商品库，不能新增或更新商品资料。");
      return;
    }

    const item = invoice?.items?.[index];
    if (!item) {
      setProductLibraryMessage("请先选择一行要保存的商品明细。");
      return;
    }

    if (!normalizeText(item.styleNo)) {
      setProductLibraryMessage("商品编码(款号)不能为空。");
      return;
    }

    setProductLibraryMessage(null);
    saveProductMutation.mutate({ item: { ...item } });
  }

  function duplicateInvoiceItem(index: number) {
    setInvoiceItems((items, invoiceId) => {
      const source = items[index];
      if (!source) {
        return items;
      }

      const duplicated: ApiInvoiceItemDto = {
        ...createEmptyInvoiceItem(invoiceId),
        ...source,
        id: 0,
        invoiceId,
      };

      return [
        ...items.slice(0, index + 1),
        duplicated,
        ...items.slice(index + 1),
      ];
    });
  }

  function moveInvoiceItem(index: number, direction: -1 | 1) {
    setInvoiceItems((items) => {
      const targetIndex = index + direction;
      if (index < 0 || index >= items.length || targetIndex < 0 || targetIndex >= items.length) {
        return items;
      }

      const nextItems = [...items];
      const current = nextItems[index];
      nextItems[index] = nextItems[targetIndex];
      nextItems[targetIndex] = current;
      return nextItems;
    });
  }

  function fillDownInvoiceItemField(index: number, field: EditableInvoiceItemField) {
    setInvoiceItems((items, invoiceId) => {
      const source = items[index - 1];
      const target = items[index];
      if (!source || !target) {
        return items;
      }

      const patch = { [field]: source[field] } as Partial<ApiInvoiceItemDto>;
      return items.map((item, itemIndex) =>
        itemIndex === index
          ? recalculateInvoiceItem({ ...createEmptyInvoiceItem(invoiceId), ...item, ...patch }, [field])
          : item,
      );
    });
  }

  function fillDownInvoiceItemCells(cells: InvoiceItemCellSelection[]) {
    if (cells.length < 2) {
      return;
    }

    setInvoiceItems((items, invoiceId) => {
      const rowsByField = new Map<EditableInvoiceItemField, Set<number>>();
      for (const cell of cells) {
        if (cell.rowIndex < 0 || cell.rowIndex >= items.length) {
          continue;
        }

        const rows = rowsByField.get(cell.field) ?? new Set<number>();
        rows.add(cell.rowIndex);
        rowsByField.set(cell.field, rows);
      }

      const patchesByRow = new Map<number, Partial<ApiInvoiceItemDto>>();
      const changedFieldsByRow = new Map<number, Set<EditableInvoiceItemField>>();

      rowsByField.forEach((rowSet, field) => {
        const rowIndices = Array.from(rowSet).sort((left, right) => left - right);
        if (rowIndices.length < 2) {
          return;
        }

        const source = items[rowIndices[0]];
        if (!source) {
          return;
        }

        const sourceValue = source[field];
        rowIndices.slice(1).forEach((rowIndex) => {
          const target = items[rowIndex];
          if (!target || areInvoiceItemValuesEqual(target[field], sourceValue)) {
            return;
          }

          const patch = patchesByRow.get(rowIndex) ?? {};
          (patch as Record<string, unknown>)[field] = sourceValue;
          patchesByRow.set(rowIndex, patch);

          const changedFields = changedFieldsByRow.get(rowIndex) ?? new Set<EditableInvoiceItemField>();
          changedFields.add(field);
          changedFieldsByRow.set(rowIndex, changedFields);
        });
      });

      if (patchesByRow.size === 0) {
        return items;
      }

      return items.map((item, itemIndex) => {
        const patch = patchesByRow.get(itemIndex);
        const changedFields = changedFieldsByRow.get(itemIndex);
        return patch && changedFields
          ? recalculateInvoiceItem(
              {
                ...createEmptyInvoiceItem(invoiceId),
                ...item,
                ...patch,
              },
              Array.from(changedFields),
            )
          : item;
      });
    });
  }

  function pasteInvoiceItemTable(
    startRowIndex: number,
    startField: EditableInvoiceItemField,
    rows: string[][],
    targetFields = invoiceItemEditableColumns.map((column) => column.field),
  ) {
    const targetColumns = targetFields
      .map((field) => invoiceItemEditableColumns.find((column) => column.field === field))
      .filter((column): column is (typeof invoiceItemEditableColumns)[number] => Boolean(column));
    const startColumnIndex = targetColumns.findIndex((column) => column.field === startField);
    if (startColumnIndex < 0 || rows.length === 0) {
      return;
    }

    setInvoiceItems((items, invoiceId) => {
      const nextItems = [...items];
      rows.forEach((row, rowOffset) => {
        const targetIndex = Math.max(0, startRowIndex) + rowOffset;
        const current = nextItems[targetIndex] ?? createEmptyInvoiceItem(invoiceId);
        const patch: Partial<ApiInvoiceItemDto> = {};
        const changedFields: string[] = [];

        row.forEach((cell, colOffset) => {
          const column = targetColumns[startColumnIndex + colOffset];
          if (!column) {
            return;
          }

          (patch as Partial<Record<EditableInvoiceItemField, string | number | undefined>>)[column.field] =
            column.kind === "number" ? readInvoiceItemTableNumber(cell) : cell.trim();
          changedFields.push(column.field);
        });

        if (changedFields.length === 0) {
          return;
        }

        nextItems[targetIndex] = recalculateInvoiceItem(
          {
            ...createEmptyInvoiceItem(invoiceId),
            ...current,
            ...patch,
            id: current.id ?? 0,
            invoiceId,
          },
          changedFields,
        );
      });

      return nextItems;
    });
  }

  function patchInvoiceItem(index: number, next: Partial<ApiInvoiceItemDto>) {
    const changedFields = Object.keys(next);
    setInvoiceItems((items, invoiceId) => {
      if (index < 0) {
        return items;
      }

      const nextItems = [...items];
      while (nextItems.length <= index) {
        nextItems.push(createEmptyInvoiceItem(invoiceId));
      }

      return nextItems.map((item, itemIndex) =>
        itemIndex === index
          ? recalculateInvoiceItem({ ...createEmptyInvoiceItem(invoiceId), ...item, ...next }, changedFields)
          : item,
      );
    });
  }

  function clearInvoiceItemCells(cells: InvoiceItemCellSelection[]) {
    if (cells.length === 0) {
      return;
    }

    setInvoiceItems((items, invoiceId) => {
      const cellsByRow = new Map<number, Set<EditableInvoiceItemField>>();
      cells.forEach((cell) => {
        if (cell.rowIndex < 0 || cell.rowIndex >= items.length) {
          return;
        }

        const fields = cellsByRow.get(cell.rowIndex) ?? new Set<EditableInvoiceItemField>();
        fields.add(cell.field);
        cellsByRow.set(cell.rowIndex, fields);
      });

      if (cellsByRow.size === 0) {
        return items;
      }

      return items.map((item, itemIndex) => {
        const fields = cellsByRow.get(itemIndex);
        if (!fields || fields.size === 0) {
          return item;
        }

        const patch: Partial<ApiInvoiceItemDto> = {};
        const changedFields = Array.from(fields);
        changedFields.forEach((field) => {
          const column = invoiceItemEditableColumns.find((entry) => entry.field === field);
          (patch as Partial<Record<EditableInvoiceItemField, string | number | undefined>>)[field] =
            column?.kind === "number" ? undefined : "";
        });

        return recalculateInvoiceItem({ ...createEmptyInvoiceItem(invoiceId), ...item, ...patch }, changedFields);
      });
    });
  }

  function removeInvoiceItem(index: number) {
    setInvoiceItems((items) => items.filter((_, itemIndex) => itemIndex !== index));
  }

  function applyInvoiceItemsSnapshot(items: ApiInvoiceItemDto[]) {
    setInvoiceItems(() => cloneInvoiceItems(items), { trackHistory: false });
  }

  function undoInvoiceItemEdit() {
    if (!invoice || itemEditHistory.undo.length === 0) {
      return;
    }

    const previousItems = itemEditHistory.undo[itemEditHistory.undo.length - 1];
    const currentItems = cloneInvoiceItems(invoice.items ?? []);
    applyInvoiceItemsSnapshot(previousItems);
    setItemEditHistory({
      undo: itemEditHistory.undo.slice(0, -1),
      redo: [...itemEditHistory.redo, currentItems].slice(-maxInvoiceItemHistoryDepth),
    });
    setSuccessMessage(null);
  }

  function redoInvoiceItemEdit() {
    if (!invoice || itemEditHistory.redo.length === 0) {
      return;
    }

    const nextItems = itemEditHistory.redo[itemEditHistory.redo.length - 1];
    const currentItems = cloneInvoiceItems(invoice.items ?? []);
    applyInvoiceItemsSnapshot(nextItems);
    setItemEditHistory({
      undo: [...itemEditHistory.undo, currentItems].slice(-maxInvoiceItemHistoryDepth),
      redo: itemEditHistory.redo.slice(0, -1),
    });
    setSuccessMessage(null);
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    saveCurrentInvoiceDraft();
  }

  function handleBackToInvoiceList() {
    if (confirmDiscardChanges("返回发票列表")) {
      navigate("/invoices");
    }
  }

  function handleOpenCustomsCoo() {
    if (!singleWindowPermission.canView) return;

    if (confirmDiscardChanges("打开海关原产地证编辑")) {
      navigate(`/single-window/coo/${parsedInvoiceId}`);
    }
  }

  function handleOpenAgentConsignment() {
    if (!singleWindowPermission.canView) return;

    if (confirmDiscardChanges("打开代理报关委托书编辑")) {
      navigate(`/single-window/acd/${parsedInvoiceId}`);
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
      canRedoItemEdit={itemEditHistory.redo.length > 0}
      canUndoItemEdit={itemEditHistory.undo.length > 0}
      invoiceItemBlankRowCount={invoiceItemBlankRowCount}
      isEditable={isInvoiceEditable}
      isFocusedWorkbench={isInvoiceItemsWorkbenchMode}
      isProductLibraryBusy={isProductLibraryBusy}
      onChange={patchInvoice}
      onAddItem={addInvoiceItem}
      onApplyProductLibraryItem={applyProductLibraryItem}
      onChangeItem={patchInvoiceItem}
      onClearItemCells={clearInvoiceItemCells}
      onDuplicateItem={duplicateInvoiceItem}
      onFillDownItemCells={fillDownInvoiceItemCells}
      onFillDownItemField={fillDownInvoiceItemField}
      onMoveItem={moveInvoiceItem}
      onOpenFocusedWorkbench={openInvoiceItemsWorkbench}
      onPasteItemTable={pasteInvoiceItemTable}
      onRedoItemEdit={redoInvoiceItemEdit}
      onRefreshProductLibrary={refreshProductLibrary}
      onRemoveItem={removeInvoiceItem}
      onSaveItemToProductLibrary={saveInvoiceItemToProductLibrary}
      onSearchProductLibrary={searchProductLibrary}
      onUndoItemEdit={undoInvoiceItemEdit}
      productLibraryMessage={productMessage}
      productLibraryProducts={products}
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

      {message ? <div className="alert">{message}</div> : null}
      {successMessage ? <div className="success-alert">{successMessage}</div> : null}
      {!invoicePermission.canOperate ? (
        <div className="permission-readonly-notice">
          当前权限模板仅允许查看发票；表头、商品明细、状态、信用证导入和保存操作已禁用。
        </div>
      ) : null}

      {!invoice && isBusy ? <div className="loading-panel">加载中</div> : null}

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
              <nav className="invoice-editor-section-nav" aria-label="发票编辑分区">
                <button type="button" className="invoice-section-nav-item" onClick={() => scrollToInvoiceSection("invoice-header-section")}>
                  <FileText size={16} aria-hidden="true" />
                  <span>发票表头</span>
                </button>
                <button type="button" className="invoice-section-nav-item invoice-section-nav-primary" onClick={() => scrollToInvoiceSection("invoice-items-section")}>
                  <PackageSearch size={16} aria-hidden="true" />
                  <span>商品明细</span>
                </button>
                <button type="button" className="invoice-section-nav-item" onClick={() => scrollToInvoiceSection("invoice-analysis-section")}>
                  <ClipboardList size={16} aria-hidden="true" />
                  <span>利润/信用证</span>
                </button>
                <button type="button" className="invoice-section-nav-item" onClick={() => scrollToInvoiceSection("invoice-report-section")}>
                  <ScrollText size={16} aria-hidden="true" />
                  <span>预览导出</span>
                </button>
              </nav>

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

                {isNew ? (
                  <details className="invoice-new-optional-section">
                    <summary>报关与扩展字段</summary>
                    <InvoiceExtendedFieldsPanel
                      invoice={invoice}
                      isEditable={isInvoiceEditable}
                      onChange={patchInvoice}
                    />
                  </details>
                ) : (
                  <InvoiceExtendedFieldsPanel
                    invoice={invoice}
                    isEditable={isInvoiceEditable}
                    onChange={patchInvoice}
                  />
                )}
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
                  disabled={!isInvoiceEditable || !reportDesignPermission.canOperate || invoiceQuery.isFetching || saveInvoiceMutation.isPending}
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

function cloneInvoiceItems(items: ApiInvoiceItemDto[]) {
  return items.map((item) => ({ ...item }));
}

function mergeRouteInvoiceImportDraft(
  existing: ApiInvoiceDetailDto,
  importedDraft: ApiInvoiceDetailDto,
  action: RouteInvoiceImportAction,
  invoiceId: number,
) {
  const importedItems = (importedDraft.items ?? []).map((item) => ({
    ...createEmptyInvoiceItem(invoiceId),
    ...item,
    id: 0,
    invoiceId,
  }));

  if (action === "AppendItems") {
    const items = [...(existing.items ?? []), ...importedItems];
    return {
      ...existing,
      items,
      ...calculateInvoiceTotals(items),
    };
  }

  return {
    ...existing,
    ...importedDraft,
    id: existing.id,
    ownerUserId: existing.ownerUserId,
    departmentId: existing.departmentId,
    companyScope: existing.companyScope,
    rowVersion: existing.rowVersion,
    items: importedItems,
    ...calculateInvoiceTotals(importedItems),
  };
}

function buildInvoiceSnapshot(invoice: ApiInvoiceDetailDto, id: number) {
  return JSON.stringify(normalizeInvoiceForSave(invoice, id));
}

function readInvoiceItemBlankRowCount(settings?: Record<string, unknown>) {
  const system = settings && typeof settings === "object" ? settings.system : null;
  const systemSettings = system && typeof system === "object" ? (system as Record<string, unknown>) : null;
  const rawValue = systemSettings?.itemEntryBlankRowCount;
  const value = typeof rawValue === "number" ? rawValue : Number(rawValue);
  if (!Number.isFinite(value)) {
    return 20;
  }

  return Math.max(1, Math.min(500, Math.trunc(value)));
}

function areInvoiceItemValuesEqual(left: unknown, right: unknown) {
  if (typeof left === "number" || typeof right === "number") {
    const leftNumber = typeof left === "number" && Number.isFinite(left) ? left : Number(left);
    const rightNumber = typeof right === "number" && Number.isFinite(right) ? right : Number(right);
    return Number.isFinite(leftNumber) && Number.isFinite(rightNumber)
      ? leftNumber === rightNumber
      : String(left ?? "") === String(right ?? "");
  }

  return String(left ?? "") === String(right ?? "");
}

function areInvoiceItemsEqual(left: ApiInvoiceItemDto[], right: ApiInvoiceItemDto[]) {
  if (left === right) {
    return true;
  }

  return JSON.stringify(left) === JSON.stringify(right);
}

function readInvoiceItemTableNumber(value: string) {
  if (!value.trim()) {
    return undefined;
  }

  return readNumber(value);
}
