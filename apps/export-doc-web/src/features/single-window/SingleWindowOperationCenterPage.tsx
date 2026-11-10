import { RecordWorkspace } from "../../ui/layout/Workspaces.tsx";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { HardDrive, RefreshCw, Search, Server } from "lucide-react";
import { type FormEvent, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { getDesktopRuntimeContext, isDesktopBridgeAvailable } from "../../desktop/desktopBridge.ts";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { listPageSizeOptions, normalizeListPageSize } from "../../ui/listViewState.ts";
import {
  batchStatusOptions,
  businessTypeOptions,
  loadSingleWindowOperationCenterViewState,
  saveSingleWindowOperationCenterViewState,
} from "./singleWindowOperationCenterModel.ts";
import { FilterSelect, OperationCenterListActionsPanel, OperationCenterTable } from "./SingleWindowOperationCenterList.tsx";
import { SingleWindowTabs } from "./SingleWindowNavigation.tsx";
import { SingleWindowStationProfilePanel } from "./SingleWindowStationProfilePanel.tsx";

import { StationSubmitPackageImportPanel, OfficeReceiptPackageImportPanel } from "./SingleWindowPackageImportPanels.tsx";

export function SingleWindowOperationCenterPage({ client }: { client: ExportDocManagerApiClient }) {
  const permission = useModulePermission("document.single-window");
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
    queryFn: ({ signal }) => client.getHealth({ signal }),
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
    queryFn: ({ signal }) => client.listSingleWindowOperationCenter({
      businessType: businessType || undefined,
      status: status || undefined,
      keyword: committedKeyword.trim() || undefined,
      pageNumber,
      pageSize,
    }, { signal }),
    placeholderData: keepPreviousData,
  });

  useServerPageNumber(operationCenterQuery, pageNumber, setPageNumber);

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
    <RecordWorkspace className="single-window-surface" aria-label="单一窗口操作中心">
      <SingleWindowTabs activeKey="operation-center" />

      <div className={isDesktopStation ? "single-window-mode-banner station-mode" : "single-window-mode-banner office-mode"}>
        {isDesktopStationResolving
          ? <RefreshCw size={20} aria-hidden="true" />
          : isDesktopStation ? <HardDrive size={20} aria-hidden="true" /> : <Server size={20} aria-hidden="true" />}
        <div>
          <strong>{isDesktopStationResolving ? "正在确认当前设备" : isDesktopStation ? "持卡机本地模式" : "办公室归档模式"}</strong>
          <span>{isDesktopStationResolving
            ? "正在确认当前设备可用的操作功能。"
            : isDesktopStation
              ? "选择当前公司与操作卡档案，导入对应待办包，把申报文件送入官方客户端交接目录，再由操作员确认导入和提交。"
              : "生成业务提交包并导入持卡机返回的回执包；办公室端不显示持卡机本地目录。"}</span>
        </div>
      </div>

      {isUnsupportedDesktopStation ? (
        <InlineNotice tone="warning" title="当前桌面环境不能作为持卡机">
          当前设备不支持持卡机操作，仍可制作或归档交接包；实体卡和官方客户端目录相关操作已隐藏。
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
          <input aria-label="搜索单一窗口批次" value={keyword} onChange={(event) => setKeyword(event.target.value)} placeholder="资料编号、合同号、批次号、回执号" />
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
      <Pagination
        pageNumber={page?.pageNumber ?? pageNumber}
        totalPages={Math.max(page?.totalPages ?? 1, 1)}
        totalCount={page?.totalCount ?? 0}
        pageSize={pageSize}
        pageSizeOptions={listPageSizeOptions}
        isBusy={isBusy}
        onPageChange={setPageNumber}
        onPageSizeChange={(value) => { setPageSize(normalizeListPageSize(value)); setPageNumber(1); }}
      />
    </RecordWorkspace>
  );
}
