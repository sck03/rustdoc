import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { FileInput, HardDrive, RefreshCw, Search, Server, Upload } from "lucide-react";
import { type FormEvent, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { ApiSingleWindowImportedPackageResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { getDesktopRuntimeContext, isDesktopBridgeAvailable, selectSingleWindowPackageFile } from "../../desktop/desktopBridge.ts";
import { DesktopIconButton, readDesktopError } from "../../ui/DesktopPathActions.tsx";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { PathField } from "../../ui/PathField.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { listPageSizeOptions, normalizeListPageSize } from "../../ui/listViewState.ts";
import {
  batchStatusOptions,
  businessTypeOptions,
  formatBatchStatus,
  formatBusinessType,
  loadSingleWindowOperationCenterViewState,
  saveSingleWindowOperationCenterViewState,
} from "./singleWindowOperationCenterModel.ts";
import { FilterSelect, OperationCenterListActionsPanel, OperationCenterTable } from "./SingleWindowOperationCenterList.tsx";
import { SingleWindowTabs } from "./SingleWindowNavigation.tsx";
import { SingleWindowStationProfilePanel } from "./SingleWindowStationProfilePanel.tsx";

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
  const desktopBridgeAvailable = isDesktopBridgeAvailable();
  const desktopRuntimeQuery = useQuery({
    queryKey: ["desktop", "runtime-context"],
    queryFn: () => getDesktopRuntimeContext(),
    enabled: desktopBridgeAvailable,
    staleTime: Number.POSITIVE_INFINITY,
  });
  const healthQuery = useQuery({
    queryKey: queryKeys.health(),
    queryFn: () => client.getHealth(),
    enabled: desktopBridgeAvailable,
    staleTime: 60_000,
  });
  const isDesktopStation = Boolean(
    desktopBridgeAvailable &&
    desktopRuntimeQuery.data?.singleWindowStationCapable &&
    desktopRuntimeQuery.data.platform === "windows" &&
    healthQuery.data?.sqliteDatabasePath,
  );
  const isDesktopStationResolving = Boolean(
    desktopBridgeAvailable && (desktopRuntimeQuery.isPending || healthQuery.isPending),
  );
  const isUnsupportedDesktopStation = Boolean(
    desktopBridgeAvailable &&
    !desktopRuntimeQuery.isPending &&
    !healthQuery.isPending &&
    !isDesktopStation,
  );

  const operationCenterQuery = useQuery({
    queryKey: queryKeys.singleWindowOperationCenter(pageNumber, pageSize, committedKeyword.trim(), businessType, status),
    queryFn: () => client.listSingleWindowOperationCenter({
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
    saveSingleWindowOperationCenterViewState({ keyword: committedKeyword, businessType, status, pageSize });
  }, [businessType, committedKeyword, pageSize, status]);

  const rows = operationCenterQuery.data?.rows ?? [];
  useEffect(() => {
    if (rows.length === 0) {
      setSelectedBatchId(null);
    } else if (!selectedBatchId || !rows.some((row) => row.batchId === selectedBatchId)) {
      setSelectedBatchId(rows[0].batchId);
    }
  }, [rows, selectedBatchId]);

  function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setCommittedKeyword(keyword.trim());
    setPageNumber(1);
  }

  const page = operationCenterQuery.data;
  const selectedRow = rows.find((row) => row.batchId === selectedBatchId) ?? null;
  const isBusy = operationCenterQuery.isFetching;

  return (
    <section className="work-surface single-window-surface" aria-label="单一窗口操作中心">
      <SingleWindowTabs activeKey="operation-center" />

      <div className={isDesktopStation ? "single-window-mode-banner station-mode" : "single-window-mode-banner office-mode"}>
        {isDesktopStationResolving
          ? <RefreshCw size={20} aria-hidden="true" />
          : isDesktopStation ? <HardDrive size={20} aria-hidden="true" /> : <Server size={20} aria-hidden="true" />}
        <div>
          <strong>{isDesktopStationResolving ? "正在识别桌面运行模式" : isDesktopStation ? "持卡机本地模式" : "办公室归档模式"}</strong>
          <span>{isDesktopStationResolving
            ? "正在核对 Windows 桌面能力和 SQLite 单机数据库，确认前不会显示持卡机或办公室专属操作。"
            : isDesktopStation
              ? "选择当前公司与操作卡档案，导入对应待办包，把申报文件送入官方客户端交接目录，再由操作员确认导入和提交。"
              : "生成业务提交包并导入持卡机返回的回执包；办公室端不显示持卡机本地目录。"}</span>
        </div>
      </div>

      {isUnsupportedDesktopStation ? (
        <InlineNotice tone="warning" title="当前桌面环境不能作为持卡机">
          持卡机操作要求 Windows 桌面版和 SQLite 单机数据库。当前环境仍可作为办公室端制作或归档交接包，但不会显示实体卡和官方客户端目录操作。
        </InlineNotice>
      ) : null}

      {!isDesktopStationResolving && isDesktopStation
        ? <SingleWindowStationProfilePanel client={client} canOperate={permission.canOperate} />
        : null}
      {!isDesktopStationResolving
        ? isDesktopStation
          ? <StationSubmitPackageImportPanel client={client} canOperate={permission.canOperate} />
          : <OfficeReceiptPackageImportPanel client={client} canOperate={permission.canOperate} />
        : null}

      <div className="toolbar single-window-toolbar">
        <form className="search-form" onSubmit={handleSearch}>
          <Search size={17} aria-hidden="true" />
          <input aria-label="搜索单一窗口批次" value={keyword} onChange={(event) => setKeyword(event.target.value)} placeholder="发票号、合同号、批次号、回执号" />
        </form>
        <div className="filter-bar">
          <FilterSelect label="业务" value={businessType} options={businessTypeOptions} onChange={(value) => { setBusinessType(value); setPageNumber(1); }} />
          <FilterSelect label="状态" value={status} options={batchStatusOptions} onChange={(value) => { setStatus(value); setPageNumber(1); }} />
        </div>
        <button className="icon-button" type="button" title="刷新" aria-label="刷新" disabled={isBusy} onClick={() => void operationCenterQuery.refetch()}>
          <RefreshCw size={18} aria-hidden="true" />
        </button>
      </div>

      {operationCenterQuery.isError ? <InlineNotice tone="error" title="操作中心加载失败">{readApiError(operationCenterQuery.error)}</InlineNotice> : null}
      {!permission.canOperate ? <PermissionNotice>当前权限仅允许查看批次和回执，交接包处理已禁用。</PermissionNotice> : null}

      {selectedRow && !isDesktopStationResolving ? (
        <OperationCenterListActionsPanel
          client={client}
          row={selectedRow}
          canOperate={permission.canOperate}
          isDesktopStation={isDesktopStation}
          onOpenDetail={() => navigate(`/single-window/operation-center/${selectedRow.batchId}`)}
        />
      ) : null}

      <OperationCenterTable data={rows} isBusy={isBusy} selectedBatchId={selectedBatchId} onSelect={setSelectedBatchId} onOpen={(batchId) => navigate(`/single-window/operation-center/${batchId}`)} />
      <ListPaginationControls
        pageNumber={page?.pageNumber ?? pageNumber}
        totalPages={Math.max(page?.totalPages ?? 1, 1)}
        totalCount={page?.totalCount ?? 0}
        pageSize={pageSize}
        pageSizeOptions={listPageSizeOptions}
        isBusy={isBusy}
        onPageChange={setPageNumber}
        onPageSizeChange={(value) => { setPageSize(normalizeListPageSize(value)); setPageNumber(1); }}
      />
    </section>
  );
}

function StationSubmitPackageImportPanel({ client, canOperate }: { client: ExportDocManagerApiClient; canOperate: boolean }) {
  const queryClient = useQueryClient();
  const [packagePath, setPackagePath] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [result, setResult] = useState<ApiSingleWindowImportedPackageResponse | null>(null);

  const profilesQuery = useQuery({
    queryKey: queryKeys.singleWindowClientProfiles(),
    queryFn: () => client.getSingleWindowClientProfiles(),
    staleTime: 60_000,
  });
  const activeProfile = profilesQuery.data?.profiles.find((profile) => profile.isActive) ?? null;

  const mutation = useMutation({
    mutationFn: () => client.importSingleWindowSubmitPackage({ body: { packagePath: packagePath.trim(), keepWorkingDirectory: false } }),
    onSuccess: async (response) => {
      setResult(response);
      setMessage(response.message || "提交包已绑定到本持卡机。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() });
    },
    onError: (error) => { setResult(null); setMessage(readApiError(error)); },
  });

  async function choosePackage() {
    try {
      const selected = await selectSingleWindowPackageFile();
      if (selected) setPackagePath(selected);
    } catch (error) {
      setMessage(readDesktopError(error));
    }
  }

  return (
    <section className="form-section single-window-intake-card" aria-label="持卡机提交包导入">
      <div className="section-header">
        <div><h2>导入待办提交包</h2><span>导入时核对当前档案、公司抬头、业务类型和申报文件完整性</span></div>
        <button className="command-button" type="button" disabled={!canOperate || mutation.isPending || !packagePath.trim() || !activeProfile} onClick={() => mutation.mutate()}><Upload size={17} aria-hidden="true" /><span>导入并绑定当前档案</span></button>
      </div>
      {activeProfile ? (
        <InlineNotice tone="info" title="当前操作档案">
          {activeProfile.profileName} · {activeProfile.companyScope} · {activeProfile.cardIdentifier}
        </InlineNotice>
      ) : <InlineNotice tone="warning">请先在上方创建并启用公司与操作卡档案。</InlineNotice>}
      <PathField label="提交包文件" value={packagePath} disabled={!canOperate || mutation.isPending} actions={<DesktopIconButton title="选择提交包" disabled={!canOperate || mutation.isPending} onClick={() => void choosePackage()}><FileInput size={17} aria-hidden="true" /></DesktopIconButton>} onChange={(value) => { setPackagePath(value); setMessage(null); }} />
      {message ? <InlineNotice tone={mutation.isError ? "error" : "success"}>{message}</InlineNotice> : null}
      {result ? <div className="single-window-import-summary"><strong>{result.manifest.invoiceNo || result.manifest.batchReference}</strong><span>{formatBusinessType(String(result.manifest.businessType === 0 ? "CustomsCoo" : "AgentConsignment"))}</span><span>{result.manifest.companyScope}</span></div> : null}
    </section>
  );
}

function OfficeReceiptPackageImportPanel({ client, canOperate }: { client: ExportDocManagerApiClient; canOperate: boolean }) {
  const queryClient = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () => client.uploadSingleWindowReceiptPackage({ fileName: file?.name, keepWorkingDirectory: false, body: file ?? new Blob() }),
    onSuccess: async (response) => {
      setMessage(`回执包已归档，新增 ${response.persistedReceiptCount} 条回执，批次状态为“${formatBatchStatus(response.trackingStatus)}”。`);
      setFile(null);
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() });
    },
    onError: (error) => setMessage(readApiError(error)),
  });

  return (
    <section className="form-section single-window-intake-card" aria-label="办公室回执包导入">
      <div className="section-header">
        <div><h2>导入持卡机回执包</h2><span>只接受能精确绑定原提交批次、公司抬头和原包摘要的 .swpkg</span></div>
        <button className="command-button" type="button" disabled={!canOperate || mutation.isPending || !file} onClick={() => mutation.mutate()}><Upload size={17} aria-hidden="true" /><span>导入回执包</span></button>
      </div>
      <label className="form-field"><span className="form-field-label"><span>回执包文件</span></span><input type="file" accept=".swpkg" disabled={!canOperate || mutation.isPending} onChange={(event) => { setFile(event.target.files?.[0] ?? null); setMessage(null); }} /></label>
      {message ? <InlineNotice tone={mutation.isError ? "error" : "success"}>{message}</InlineNotice> : null}
    </section>
  );
}
