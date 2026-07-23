import { FormEvent, KeyboardEvent, useEffect, useState } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Copy, Download, FileArchive, FileCheck2, FileSpreadsheet, FolderOpen, Plus, RefreshCw, Search, Send, Upload, X } from "lucide-react";
import { useLocation, useNavigate } from "react-router-dom";
import {
  ApiInvoiceListItemDto,
  ApiInvoiceTransferPreviewResponse,
  ApiSingleWindowHandoffPackageResponse,
  ApiSingleWindowImportedPackageResponse,
  ExportDocManagerApiClient,
  SingleWindowExportReview,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { getWorkspaceDeviceCapabilities, useWorkspaceDeviceMode } from "../../app/workspaceDevice.ts";
import {
  isDesktopBridgeAvailable,
  openPath,
  selectExcelFile,
  selectInvoiceTransferPackageFile,
  selectSaveExcelPath,
  selectSaveInvoiceTransferPackagePath,
  selectSavePackagePath,
  selectSingleWindowPackageFile,
} from "../../desktop/desktopBridge.ts";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { WorkspaceDeviceNotice } from "../../ui/WorkspaceDeviceNotice.tsx";
import { formatAmount, formatDate, readApiError, readRouteSuccessMessage } from "../../ui/formUtils.ts";
import { listPageSizeOptions, loadListViewState, normalizeListPageSize, saveListViewState } from "../../ui/listViewState.ts";
import { ViewJobButton } from "../jobs/ViewJobButton.tsx";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { downloadJobResultWhenReady } from "../../ui/downloadJobResult.ts";
import { readDefaultExportDirectory } from "../settings/settingsPaths.ts";
import { getInvoiceStatusLabel } from "./invoiceModel.ts";
import { InvoiceCopyOptionsPanel } from "./InvoiceCopyOptionsPanel.tsx";
import { InvoiceTransferImportPanel } from "./InvoiceTransferImportPanel.tsx";
import { InvoiceTable } from "./InvoiceTable.tsx";
import { SingleWindowActionsPanel, type SingleWindowActionDraft } from "./SingleWindowActionsPanel.tsx";
import { useInvoiceListSingleWindowOperations } from "./useInvoiceListSingleWindowOperations.ts";
import { readPathDialogError, requestExcelSavePath, requestPackageOpenPath, requestPackageSavePath, requestSingleWindowPackageOpenPath, requestSingleWindowPackageSavePath } from "./invoiceListDesktopPaths.ts";
import {
  buildBookingSheetDefaultFileName,
  buildSingleWindowPackageDefaultFileName,
  sanitizePackageFileName,
  type SingleWindowBusinessType,
} from "./invoiceListFileNames.ts";
import {
  buildDefaultCopyInvoiceNo,
  buildExcelImportRouteSuccessMessage,
  buildSingleWindowReviewMessage,
  createEmptyInvoiceTransferImportDraft,
  createInvoiceTransferImportDraft,
  flattenSingleWindowReviewIssues,
  formatReviewSeverity,
  formatReviewSeverityKey,
  formatSingleWindowBusinessType,
  formatSingleWindowNavigationTarget,
  getAutoRepairGroupKeys,
  matchesSingleWindowReview,
  normalizeRequiredPackagePath,
  validateInvoiceCopyDraft,
  validateInvoiceTransferImportDraft,
  type InvoiceTransferConflictAction,
  type InvoiceTransferImportDraft,
  type InvoiceCopyDraft,
} from "./invoiceListModels.ts";

const invoiceListViewStateStorageKey = "export-doc-manager.invoice-list-view-state.v1";

export function InvoiceListPage({ client }: { client: ExportDocManagerApiClient }) {
  const invoicePermission = useModulePermission("document.invoices");
  const excelPermission = useModulePermission("document.excel");
  const singleWindowPermission = useModulePermission("document.single-window");
  const workspaceDeviceMode = useWorkspaceDeviceMode();
  const workspaceDeviceCapabilities = getWorkspaceDeviceCapabilities(workspaceDeviceMode);
  const [initialListViewState] = useState(() => loadListViewState(invoiceListViewStateStorageKey));
  const [keyword, setKeyword] = useState(initialListViewState.keyword);
  const [committedKeyword, setCommittedKeyword] = useState(initialListViewState.keyword);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(initialListViewState.pageSize);
  const [copyDraft, setCopyDraft] = useState<InvoiceCopyDraft | null>(null);
  const [copyMessage, setCopyMessage] = useState<string | null>(null);
  const [transferDraft, setTransferDraft] = useState<InvoiceTransferImportDraft | null>(null);
  const [transferUploadFile, setTransferUploadFile] = useState<File | null>(null);
  const [transferMessage, setTransferMessage] = useState<string | null>(null);
  const [transferSuccessMessage, setTransferSuccessMessage] = useState<string | null>(null);
  const [lastExportedPackagePath, setLastExportedPackagePath] = useState<string | null>(null);
  const navigate = useNavigate();
  const location = useLocation();
  const routeSuccessMessage = readRouteSuccessMessage(location.state);
  const queryClient = useQueryClient();

  const invoicesQuery = useQuery({
    queryKey: queryKeys.invoices(pageNumber, pageSize, committedKeyword.trim()),
    queryFn: () =>
      client.listInvoices({
        pageNumber,
        pageSize,
        keyword: committedKeyword.trim() || undefined,
        sortColumn: "InvoiceDate",
        ascending: false,
      }),
    placeholderData: keepPreviousData,
  });

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: () => client.getSettings(),
    staleTime: 5 * 60 * 1000,
  });

  const defaultExportDirectory = readDefaultExportDirectory(settingsQuery.data?.settings);
  const singleWindow = useInvoiceListSingleWindowOperations({
    client,
    queryClient,
    defaultExportDirectory,
    canView: singleWindowPermission.canView,
    canOperate: singleWindowPermission.canOperate,
    canExportBookingSheet: excelPermission.canOperate,
  });

  useEffect(() => {
    if (invoicesQuery.data && invoicesQuery.data.pageNumber !== pageNumber) {
      setPageNumber(invoicesQuery.data.pageNumber);
    }
  }, [invoicesQuery.data, pageNumber]);

  useEffect(() => {
    saveListViewState(invoiceListViewStateStorageKey, {
      keyword: committedKeyword,
      pageSize,
    });
  }, [committedKeyword, pageSize]);

  function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const nextKeyword = keyword.trim();
    setKeyword(nextKeyword);
    setCommittedKeyword(nextKeyword);
    setPageNumber(1);
  }

  function handleResetSearch() {
    setKeyword("");
    setCommittedKeyword("");
    setPageNumber(1);
  }

  function handlePageSizeChange(value: number) {
    setPageSize(normalizeListPageSize(value));
    setPageNumber(1);
  }

  const cloneInvoiceMutation = useMutation({
    mutationFn: (draft: InvoiceCopyDraft) =>
      client.cloneInvoice({
        id: draft.source.id,
        body: {
          newInvoiceNo: draft.newInvoiceNo.trim(),
          options: {
            copyHeader: draft.copyHeader,
            copyItems: draft.copyItems,
            resetDates: draft.resetDates,
            resetStatus: draft.resetStatus,
            clearAmounts: draft.clearAmounts,
          },
        },
      }),
    onSuccess: async (response) => {
      setCopyDraft(null);
      setCopyMessage(null);
      queryClient.setQueryData(queryKeys.invoice(response.id), response.invoice);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.invoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.queryInvoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard() }),
      ]);
      navigate(`/invoices/${response.id}`, {
        state: { successMessage: response.message || "发票已复制。" },
      });
    },
    onError: (error) => {
      setCopyMessage(readApiError(error));
    },
  });

  const exportTransferPackageMutation = useMutation({
    mutationFn: async ({ invoice, packagePath }: { invoice: ApiInvoiceListItemDto; packagePath: string }) => {
      if (isDesktopBridgeAvailable()) {
        const response = await client.saveInvoiceTransferPackageToPath({ id: invoice.id, body: { packagePath } });
        return { mode: "desktop" as const, response };
      }

      const blob = await client.downloadInvoiceTransferPackage({ id: invoice.id });
      downloadBlob(blob, `${sanitizePackageFileName(invoice.invoiceNo || "invoice")}.edpkg`);
      return { mode: "browser" as const };
    },
    onSuccess: (result) => {
      setTransferMessage(null);
      if (result.mode === "desktop") {
        setTransferSuccessMessage(`${result.response.message} ${result.response.packagePath}`);
        setLastExportedPackagePath(result.response.packagePath);
      } else {
        setTransferSuccessMessage("发票单据包已交给浏览器下载。");
        setLastExportedPackagePath(null);
      }
    },
    onError: (error) => {
      setTransferMessage(readApiError(error));
      setTransferSuccessMessage(null);
      setLastExportedPackagePath(null);
    },
  });

  const previewTransferPackageMutation = useMutation({
    mutationFn: async ({ packagePath, uploadFile }: { packagePath: string; uploadFile?: File | null }) => {
      const response = isDesktopBridgeAvailable()
        ? await client.previewInvoiceTransferPackage({ body: { packagePath } })
        : await client.previewUploadedInvoiceTransferPackage({ fileName: uploadFile?.name, body: uploadFile ?? new Blob() });
      return { packagePath: uploadFile?.name || packagePath, response };
    },
    onSuccess: ({ packagePath, response }) => {
      setTransferDraft(createInvoiceTransferImportDraft(packagePath, response));
      setTransferMessage(null);
      setTransferSuccessMessage(null);
      setLastExportedPackagePath(null);
    },
    onError: (error) => {
      setTransferMessage(readApiError(error));
      setTransferSuccessMessage(null);
    },
  });

  const importTransferPackageMutation = useMutation({
    mutationFn: (draft: InvoiceTransferImportDraft) => isDesktopBridgeAvailable()
      ? client.importInvoiceTransferPackage({
        body: {
          packagePath: draft.packagePath.trim(),
          conflictAction: draft.conflictAction,
          newInvoiceNo: draft.newInvoiceNo.trim(),
          allowInvalidChecksum: draft.allowInvalidChecksum,
        },
      })
      : client.importUploadedInvoiceTransferPackage({
          fileName: transferUploadFile?.name,
          conflictAction: draft.conflictAction,
          newInvoiceNo: draft.newInvoiceNo.trim() || undefined,
          allowInvalidChecksum: draft.allowInvalidChecksum,
          body: transferUploadFile ?? new Blob(),
        }),
    onSuccess: async (response) => {
      setTransferDraft(null);
      setTransferMessage(null);
      setTransferSuccessMessage(response.message || "单据包已导入。");
      setLastExportedPackagePath(null);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.invoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.queryInvoicesRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.dashboard() }),
      ]);
      if (response.result.invoiceId && response.result.invoiceId > 0) {
        navigate(`/invoices/${response.result.invoiceId}`, {
          state: { successMessage: response.message || "单据包已导入。" },
        });
      }
    },
    onError: (error) => {
      setTransferMessage(readApiError(error));
      setTransferSuccessMessage(null);
    },
  });

  const excelImportPreviewMutation = useMutation({
    mutationFn: (filePath: string) =>
      client.previewExcelImport({
        body: { filePath },
      }),
    onSuccess: (response) => {
      if (!response.invoice) {
        setTransferMessage("Excel 已解析，但没有生成可用的发票草稿。请检查模板内容。");
        setTransferSuccessMessage(null);
        return;
      }

      setTransferDraft(null);
      setTransferMessage(null);
      setTransferSuccessMessage(null);
      setLastExportedPackagePath(null);
      navigate("/invoices/new", {
        state: {
          invoiceDraft: response.invoice,
          successMessage: buildExcelImportRouteSuccessMessage(response),
        },
      });
    },
    onError: (error) => {
      setTransferMessage(readApiError(error));
      setTransferSuccessMessage(null);
    },
  });

  function openCopyPanel(invoice: ApiInvoiceListItemDto) {
    if (!invoicePermission.canOperate) return;

    setCopyDraft({
      source: invoice,
      newInvoiceNo: buildDefaultCopyInvoiceNo(invoice.invoiceNo),
      copyHeader: true,
      copyItems: true,
      resetStatus: true,
      resetDates: true,
      clearAmounts: false,
    });
    setCopyMessage(null);
  }

  function openSingleWindowPanel(invoice: ApiInvoiceListItemDto) {
    singleWindow.open(invoice);
  }

  function patchCopyDraft(next: Partial<InvoiceCopyDraft>) {
    setCopyDraft((current) => (current ? { ...current, ...next } : current));
    setCopyMessage(null);
  }

  function submitCopyInvoice(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!invoicePermission.canOperate || !copyDraft || cloneInvoiceMutation.isPending) {
      return;
    }

    const validationMessage = validateInvoiceCopyDraft(copyDraft);
    if (validationMessage) {
      setCopyMessage(validationMessage);
      return;
    }

    setCopyMessage(null);
    cloneInvoiceMutation.mutate(copyDraft);
  }

  async function handleExportTransferPackage(invoice: ApiInvoiceListItemDto) {
    if (!invoicePermission.canOperate || exportTransferPackageMutation.isPending) {
      return;
    }

    if (!isDesktopBridgeAvailable()) {
      setTransferMessage(null);
      setTransferSuccessMessage(null);
      setLastExportedPackagePath(null);
      exportTransferPackageMutation.mutate({ invoice, packagePath: "" });
      return;
    }

    const defaultFileName = `${sanitizePackageFileName(invoice.invoiceNo || "invoice")}.edpkg`;
    const packagePath = await requestPackageSavePath(defaultFileName, defaultExportDirectory).catch((error) => {
      setTransferMessage(readPathDialogError(error));
      setTransferSuccessMessage(null);
      return "";
    });
    if (!packagePath) {
      return;
    }

    setTransferMessage(null);
    setTransferSuccessMessage(null);
    setLastExportedPackagePath(null);
    exportTransferPackageMutation.mutate({ invoice, packagePath });
  }

  async function handleChooseImportTransferPackage() {
    if (!invoicePermission.canOperate || previewTransferPackageMutation.isPending || importTransferPackageMutation.isPending) {
      return;
    }

    if (!isDesktopBridgeAvailable()) {
      setTransferDraft(createEmptyInvoiceTransferImportDraft());
      setTransferUploadFile(null);
      setTransferMessage("请选择本机 .edpkg 单据包上传预览；浏览器不会读取服务器绝对路径。");
      setTransferSuccessMessage(null);
      setLastExportedPackagePath(null);
      return;
    }

    const packagePath = await requestPackageOpenPath().catch((error) => {
      setTransferMessage(readPathDialogError(error));
      setTransferSuccessMessage(null);
      return "";
    });
    if (!packagePath) {
      return;
    }

    setTransferMessage(null);
    setTransferSuccessMessage(null);
    previewTransferPackageMutation.mutate({ packagePath });
  }

  async function handleChooseExcelImport() {
    if (!invoicePermission.canOperate || !excelPermission.canOperate || excelImportPreviewMutation.isPending) {
      return;
    }

    if (!isDesktopBridgeAvailable()) {
      setTransferMessage("当前环境不能打开本机文件选择器，请到 Excel 导入工具页输入模板路径。");
      setTransferSuccessMessage(null);
      return;
    }

    const filePath = await selectExcelFile().catch((error) => {
      setTransferMessage(readPathDialogError(error));
      setTransferSuccessMessage(null);
      return "";
    });
    if (!filePath) {
      return;
    }

    setTransferMessage(null);
    setTransferSuccessMessage(null);
    setLastExportedPackagePath(null);
    excelImportPreviewMutation.mutate(filePath);
  }

  function handlePreviewTransferPackage(packagePath: string) {
    if (!invoicePermission.canOperate) return;

    const normalized = normalizeRequiredPackagePath(packagePath);
    if (normalized.error) {
      setTransferMessage(normalized.error);
      setTransferSuccessMessage(null);
      return;
    }

    setTransferMessage(null);
    setTransferSuccessMessage(null);
    previewTransferPackageMutation.mutate({ packagePath: normalized.value });
  }

  function handlePreviewTransferUpload(file: File | null) {
    setTransferUploadFile(file);
    setTransferMessage(null);
    setTransferSuccessMessage(null);
    if (file) {
      previewTransferPackageMutation.mutate({ packagePath: file.name, uploadFile: file });
    }
  }

  function patchTransferDraft(next: Partial<InvoiceTransferImportDraft>) {
    setTransferDraft((current) => (current ? { ...current, ...next } : current));
    setTransferMessage(null);
    setTransferSuccessMessage(null);
  }

  function submitTransferImport(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!invoicePermission.canOperate || !transferDraft || importTransferPackageMutation.isPending) {
      return;
    }

    const validationMessage = validateInvoiceTransferImportDraft(transferDraft);
    if (validationMessage) {
      setTransferMessage(validationMessage);
      setTransferSuccessMessage(null);
      return;
    }

    setTransferMessage(null);
    setTransferSuccessMessage(null);
    importTransferPackageMutation.mutate(transferDraft);
  }

  async function handleOpenLastExportedPackage() {
    if (!lastExportedPackagePath) {
      return;
    }

    try {
      await openPath(lastExportedPackagePath);
    } catch (error) {
      setTransferMessage(error instanceof Error ? error.message : "打开单据包失败。");
      setTransferSuccessMessage(null);
    }
  }

  const invoices = invoicesQuery.data ?? null;
  const message = invoicesQuery.isError ? readApiError(invoicesQuery.error) : copyMessage ?? transferMessage;
  const isBusy =
    invoicesQuery.isFetching ||
    cloneInvoiceMutation.isPending ||
    exportTransferPackageMutation.isPending ||
    previewTransferPackageMutation.isPending ||
    importTransferPackageMutation.isPending ||
    excelImportPreviewMutation.isPending ||
    singleWindow.isBusy;

  return (
    <section className="work-surface" aria-label="发票列表">
      {!invoicePermission.canOperate ? (
        <PermissionNotice>当前权限模板仅允许查看发票；新建、复制、导入和单据包维护已禁用。</PermissionNotice>
      ) : null}
      <WorkspaceDeviceNotice
        mode={workspaceDeviceMode}
        phone="可查看、搜索和打开已有发票进行简单回填；新建完整发票、Excel/单据包导入和批量输出请使用桌面端。"
        tablet="可查看、搜索、新建基础草稿和进行轻量编辑；Excel/单据包导入和批量输出请使用桌面端。"
      />
      <div className="toolbar">
        <form className="search-form" onSubmit={handleSearch}>
          <Search size={17} aria-hidden="true" />
          <input
            aria-label="搜索发票"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            placeholder="发票号、合同号、客户、港口"
          />
        </form>
        <div className="toolbar-actions">
          <button
            className="icon-button"
            type="button"
            title="重置搜索" aria-label="重置搜索"
            disabled={isBusy || (!keyword && !committedKeyword)}
            onClick={handleResetSearch}
          >
            <X size={18} aria-hidden="true" />
          </button>
          {workspaceDeviceCapabilities.canImportExport && invoicePermission.canOperate && excelPermission.canOperate ? <button
            className="command-button secondary"
            type="button"
            disabled={isBusy}
            onClick={() => void handleChooseExcelImport()}
          >
            <FileSpreadsheet size={17} aria-hidden="true" />
            <span>导入 Excel</span>
          </button> : null}
          {workspaceDeviceCapabilities.canImportExport && invoicePermission.canOperate ? <button
            className="command-button secondary"
            type="button"
            disabled={isBusy}
            onClick={() => void handleChooseImportTransferPackage()}
          >
            <Upload size={17} aria-hidden="true" />
            <span>导入单据包</span>
          </button> : null}
          <button
            className="icon-button"
            type="button"
            title="刷新" aria-label="刷新"
            disabled={isBusy}
            onClick={() => void invoicesQuery.refetch()}
          >
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          {invoicePermission.canOperate && workspaceDeviceMode !== "phone" ? <button className="command-button" type="button" onClick={() => navigate("/invoices/new")}>
            <Plus size={17} aria-hidden="true" />
            <span>新建</span>
          </button> : null}
        </div>
      </div>

      {message ? <InlineNotice tone="error" title="发票操作未完成">{message}</InlineNotice> : null}
      {routeSuccessMessage ? <InlineNotice tone="success">{routeSuccessMessage}</InlineNotice> : null}
      {transferSuccessMessage ? (
        <InlineNotice
          tone="success"
          action={lastExportedPackagePath && isDesktopBridgeAvailable() ? (
            <button className="text-button compact-text-button" type="button" onClick={() => void handleOpenLastExportedPackage()}>
              <FolderOpen size={15} aria-hidden="true" />
              <span>打开</span>
            </button>
          ) : undefined}
        >
          {transferSuccessMessage}
        </InlineNotice>
      ) : null}
      {copyDraft ? (
        <InvoiceCopyOptionsPanel
          draft={copyDraft}
          isBusy={cloneInvoiceMutation.isPending}
          onCancel={() => {
            setCopyDraft(null);
            setCopyMessage(null);
          }}
          onChange={patchCopyDraft}
          onSubmit={submitCopyInvoice}
        />
      ) : null}
      {transferDraft ? (
        <InvoiceTransferImportPanel
          draft={transferDraft}
          isBusy={previewTransferPackageMutation.isPending || importTransferPackageMutation.isPending}
          uploadMode={!isDesktopBridgeAvailable()}
          uploadFile={transferUploadFile}
          onUploadFileChange={handlePreviewTransferUpload}
          onCancel={() => {
            setTransferDraft(null);
            setTransferUploadFile(null);
            setTransferMessage(null);
          }}
          onChange={patchTransferDraft}
          onPreview={handlePreviewTransferPackage}
          onSubmit={submitTransferImport}
        />
      ) : null}
      {singleWindow.draft ? (
        <SingleWindowActionsPanel
          draft={singleWindow.draft}
          isBusy={singleWindow.isActionBusy}
          message={singleWindow.message}
          messageType={singleWindow.messageType}
          jobId={singleWindow.jobId}
          packagePath={singleWindow.packagePath}
          review={singleWindow.review}
          reviewBusinessType={singleWindow.reviewBusinessType}
          reviewInvoiceId={singleWindow.reviewInvoiceId}
          isReviewBusy={singleWindow.isReviewBusy}
          canOperate={singleWindowPermission.canOperate}
          canExportBookingSheet={excelPermission.canOperate}
          onCancel={() => {
            singleWindow.close();
          }}
          onEditCustomsCoo={(invoice) => navigate(`/single-window/coo/${invoice.id}`)}
          onEditAgentConsignment={(invoice) => navigate(`/single-window/acd/${invoice.id}`)}
          onReviewCustomsCoo={(invoice) => singleWindow.buildReview(invoice, "CustomsCoo")}
          onReviewAgentConsignment={(invoice) => singleWindow.buildReview(invoice, "AgentConsignment")}
          onRepairReview={singleWindow.repairReview}
          onExportCustomsCoo={(invoice) => void singleWindow.exportPackage(invoice, "CustomsCoo")}
          onExportAgentConsignment={(invoice) => void singleWindow.exportPackage(invoice, "AgentConsignment")}
          onImportReceiptPackage={() => void singleWindow.importReceipt()}
          onOpenOperationCenter={() => navigate("/single-window/operation-center")}
          onOpenPackagePath={() => void singleWindow.openPackagePath()}
          onExportBookingSheet={(invoice) => void singleWindow.exportBookingSheet(invoice)}
        />
      ) : null}

      <InvoiceTable
        data={invoices?.items ?? []}
        isBusy={isBusy}
        hasError={Boolean(invoicesQuery.isError)}
        canOperate={invoicePermission.canOperate && workspaceDeviceMode !== "phone"}
        canExportPackage={invoicePermission.canOperate && workspaceDeviceCapabilities.canImportExport}
        canExportBookingSheet={excelPermission.canOperate && workspaceDeviceCapabilities.canImportExport}
        canUseSingleWindow={singleWindowPermission.canView}
        onOpen={(invoiceId) => navigate(`/invoices/${invoiceId}`)}
        onCopy={openCopyPanel}
        onExportPackage={(invoice) => void handleExportTransferPackage(invoice)}
        onExportBookingSheet={(invoice) => void singleWindow.exportBookingSheet(invoice)}
        onSingleWindow={openSingleWindowPanel}
      />

      <ListPaginationControls
        pageNumber={invoices?.pageNumber ?? pageNumber}
        totalPages={Math.max(invoices?.totalPages ?? 1, 1)}
        totalCount={invoices?.totalCount ?? 0}
        pageSize={pageSize}
        pageSizeOptions={listPageSizeOptions}
        isBusy={isBusy}
        onPageChange={setPageNumber}
        onPageSizeChange={handlePageSizeChange}
      />
    </section>
  );
}
