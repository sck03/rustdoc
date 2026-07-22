import { keepPreviousData,useMutation,useQuery,useQueryClient } from "@tanstack/react-query";
import { FileInput,FolderOpen,RefreshCw,Search,Upload } from "lucide-react";
import { FormEvent,useEffect,useState } from "react";
import { useNavigate } from "react-router-dom";
import {
ApiSingleWindowImportedPackageResponse,
ExportDocManagerApiClient
} from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { queryKeys } from "../../api/queryKeys.ts";
import {
isDesktopBridgeAvailable,
selectDirectory,
selectSingleWindowPackageFile
} from "../../desktop/desktopBridge.ts";
import { DesktopIconButton,readDesktopError,renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { PathField } from "../../ui/PathField.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { listPageSizeOptions,normalizeListPageSize } from "../../ui/listViewState.ts";

import {
batchStatusOptions,
businessTypeOptions,
loadSingleWindowOperationCenterViewState,
saveSingleWindowOperationCenterViewState
} from "./singleWindowOperationCenterModel.ts";

import { SingleWindowTabs } from "./SingleWindowNavigation.tsx";
import { FilterSelect,OperationCenterListActionsPanel,OperationCenterTable } from "./SingleWindowOperationCenterList.tsx";
import { PackageImportResult } from "./SingleWindowReceiptResults.tsx";

export function SingleWindowOperationCenterPage({ client }: { client: ExportDocManagerApiClient }) {
  const permission = useModulePermission("document.single-window");
  const queryClient = useQueryClient();
  const [initialListViewState] = useState(() => loadSingleWindowOperationCenterViewState());
  const [keyword, setKeyword] = useState(initialListViewState.keyword);
  const [committedKeyword, setCommittedKeyword] = useState(initialListViewState.keyword);
  const [businessType, setBusinessType] = useState(initialListViewState.businessType);
  const [status, setStatus] = useState(initialListViewState.status);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(initialListViewState.pageSize);
  const [selectedBatchId, setSelectedBatchId] = useState<number | null>(null);
  const navigate = useNavigate();

  const operationCenterQuery = useQuery({
    queryKey: queryKeys.singleWindowOperationCenter(pageNumber, pageSize, committedKeyword.trim(), businessType, status),
    queryFn: () =>
      client.listSingleWindowOperationCenter({
        businessType: businessType || undefined,
        status: status || undefined,
        keyword: committedKeyword.trim() || undefined,
        pageNumber,
        pageSize,
      }),
    placeholderData: keepPreviousData,
  });

  useEffect(() => {
    if (operationCenterQuery.data && operationCenterQuery.data.pageNumber !== pageNumber) {
      setPageNumber(operationCenterQuery.data.pageNumber);
    }
  }, [operationCenterQuery.data, pageNumber]);

  useEffect(() => {
    saveSingleWindowOperationCenterViewState({
      keyword: committedKeyword,
      businessType,
      status,
      pageSize,
    });
  }, [businessType, committedKeyword, pageSize, status]);

  function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setCommittedKeyword(keyword.trim());
    setPageNumber(1);
  }

  function changeBusinessType(value: string) {
    setBusinessType(value);
    setPageNumber(1);
  }

  function changeStatus(value: string) {
    setStatus(value);
    setPageNumber(1);
  }

  function handlePageSizeChange(nextPageSize: number) {
    setPageSize(normalizeListPageSize(nextPageSize));
    setPageNumber(1);
  }

  const page = operationCenterQuery.data ?? null;
  const rows = page?.rows ?? [];
  const selectedRow = rows.find((row) => row.batchId === selectedBatchId) ?? null;
  const totalPages = Math.max(page?.totalPages ?? 1, 1);
  const message = operationCenterQuery.isError ? readApiError(operationCenterQuery.error) : null;
  const isBusy = operationCenterQuery.isFetching;

  useEffect(() => {
    if (rows.length === 0) {
      setSelectedBatchId(null);
      return;
    }

    if (!selectedBatchId || !rows.some((row) => row.batchId === selectedBatchId)) {
      setSelectedBatchId(rows[0].batchId);
    }
  }, [rows, selectedBatchId]);

  return (
    <section className="work-surface single-window-surface" aria-label="单一窗口操作中心">
      <SingleWindowTabs activeKey="operation-center" />

      <div className="toolbar single-window-toolbar">
        <form className="search-form" onSubmit={handleSearch}>
          <Search size={17} aria-hidden="true" />
          <input
            aria-label="搜索单一窗口批次"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            placeholder="发票号、合同号、批次号、回执号"
          />
        </form>
        <div className="filter-bar">
          <FilterSelect
            label="业务"
            value={businessType}
            options={businessTypeOptions}
            onChange={changeBusinessType}
          />
          <FilterSelect label="状态" value={status} options={batchStatusOptions} onChange={changeStatus} />
        </div>
        <div className="toolbar-actions">
          <button
            className="icon-button"
            type="button"
            title="刷新" aria-label="刷新"
            disabled={isBusy}
            onClick={() => void operationCenterQuery.refetch()}
          >
            <RefreshCw size={18} aria-hidden="true" />
          </button>
        </div>
      </div>

      {message ? <InlineNotice tone="error" title="操作中心加载失败">{message}</InlineNotice> : null}
      {!permission.canOperate ? <PermissionNotice>当前权限模板仅允许查看批次和回执；提交包导入、派发、收件和目录维护已禁用。</PermissionNotice> : null}

      <SubmitPackageImportPanel client={client} queryClient={queryClient} canOperate={permission.canOperate} />

      {selectedRow ? (
        <OperationCenterListActionsPanel
          client={client}
          row={selectedRow}
          canOperate={permission.canOperate}
          onOpenDetail={() => navigate(`/single-window/operation-center/${selectedRow.batchId}`)}
        />
      ) : null}

      <OperationCenterTable
        data={rows}
        isBusy={isBusy}
        selectedBatchId={selectedBatchId}
        onSelect={setSelectedBatchId}
        onOpen={(batchId) => navigate(`/single-window/operation-center/${batchId}`)}
      />

      <ListPaginationControls
        pageNumber={page?.pageNumber ?? pageNumber}
        totalPages={totalPages}
        totalCount={page?.totalCount ?? 0}
        pageSize={pageSize}
        pageSizeOptions={listPageSizeOptions}
        isBusy={isBusy}
        onPageChange={setPageNumber}
        onPageSizeChange={handlePageSizeChange}
      />
    </section>
  );
}

function SubmitPackageImportPanel({
  client,
  queryClient,
  canOperate,
}: {
  client: ExportDocManagerApiClient;
  queryClient: ReturnType<typeof useQueryClient>;
  canOperate: boolean;
}) {
  const [packagePath, setPackagePath] = useState("");
  const [workingDirectory, setWorkingDirectory] = useState("");
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [desktopMessage, setDesktopMessage] = useState<string | null>(null);
  const [result, setResult] = useState<ApiSingleWindowImportedPackageResponse | null>(null);
  const isDesktop = isDesktopBridgeAvailable();

  const importMutation = useMutation({
    mutationFn: () => isDesktop
      ? client.importSingleWindowSubmitPackage({
        body: {
          packagePath: packagePath.trim(),
          workingDirectory: workingDirectory.trim() || undefined,
          keepWorkingDirectory: true,
        },
      })
      : client.uploadSingleWindowSubmitPackage({
          fileName: uploadFile?.name,
          keepWorkingDirectory: false,
          body: uploadFile ?? new Blob(),
        }),
    onSuccess: async (response) => {
      setResult(response);
      setMessage(response.message || "提交包已导入。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() });
      if (response.trackingBatchId) {
        await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterDetail(response.trackingBatchId) });
      }
    },
    onError: (error) => {
      setResult(null);
      setMessage(readApiError(error));
    },
  });

  function importSubmitPackage() {
    if (!canOperate) return;

    setMessage(null);
    if (isDesktop ? !packagePath.trim() : !uploadFile) {
      setResult(null);
      setMessage(isDesktop ? "提交包路径不能为空。" : "请选择要上传的提交包。");
      return;
    }

    importMutation.mutate();
  }

  async function choosePackagePath() {
    if (!canOperate) return;

    try {
      const selectedPath = await selectSingleWindowPackageFile();
      if (selectedPath) {
        setPackagePath(selectedPath);
        setMessage(null);
        setDesktopMessage(null);
      }
    } catch (error) {
      setDesktopMessage(readDesktopError(error));
    }
  }

  async function chooseWorkingDirectory() {
    if (!canOperate) return;

    try {
      const selectedPath = await selectDirectory();
      if (selectedPath) {
        setWorkingDirectory(selectedPath);
        setMessage(null);
        setDesktopMessage(null);
      }
    } catch (error) {
      setDesktopMessage(readDesktopError(error));
    }
  }

  return (
    <section className="form-section single-window-import-section" aria-label="提交包导入">
      <div className="section-header">
        <h2>提交包导入</h2>
        <button className="command-button secondary" type="button" disabled={!canOperate || importMutation.isPending} onClick={importSubmitPackage}>
          <Upload size={17} aria-hidden="true" />
          <span>导入提交包</span>
        </button>
      </div>

      {message ? <InlineNotice tone={importMutation.isError || !result ? "error" : "success"}>{message}</InlineNotice> : null}
      {desktopMessage ? <InlineNotice tone="error" title="提交包导入失败">{desktopMessage}</InlineNotice> : null}

      <div className="field-grid">
        {isDesktop ? <PathField
          label="提交包路径"
          value={packagePath}
          disabled={!canOperate || importMutation.isPending}
          actions={
            isDesktop ? (
              <>
                <DesktopIconButton title="选择提交包" disabled={!canOperate || importMutation.isPending} onClick={choosePackagePath}>
                  <FileInput size={17} aria-hidden="true" />
                </DesktopIconButton>
                {renderOpenPathAction(packagePath, "打开提交包位置", setDesktopMessage)}
              </>
            ) : undefined
          }
          onChange={(value) => {
            setPackagePath(value);
            setMessage(null);
            setDesktopMessage(null);
          }}
        /> : <label className="inline-filter"><span>提交包文件</span><input type="file" accept=".swpkg" disabled={!canOperate || importMutation.isPending} onChange={(event) => { setUploadFile(event.target.files?.[0] ?? null); setMessage(null); }} /></label>}
        {isDesktop ? <PathField
          label="导入工作目录"
          value={workingDirectory}
          disabled={!canOperate || importMutation.isPending}
          actions={
            isDesktop ? (
              <>
                <DesktopIconButton title="选择工作目录" disabled={!canOperate || importMutation.isPending} onClick={chooseWorkingDirectory}>
                  <FolderOpen size={17} aria-hidden="true" />
                </DesktopIconButton>
                {renderOpenPathAction(workingDirectory, "打开工作目录", setDesktopMessage)}
              </>
            ) : undefined
          }
          onChange={(value) => {
            setWorkingDirectory(value);
            setMessage(null);
            setDesktopMessage(null);
          }}
        /> : null}
      </div>

      {result && isDesktop ? <PackageImportResult result={result} includeReceipts={false} onOpenError={setDesktopMessage} /> : null}
    </section>
  );
}
