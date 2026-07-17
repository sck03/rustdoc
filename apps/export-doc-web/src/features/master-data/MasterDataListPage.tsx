import { keepPreviousData,useMutation,useQuery,useQueryClient } from "@tanstack/react-query";
import { Edit3,Plus,RefreshCw,Search,Trash2 } from "lucide-react";
import { FormEvent,KeyboardEvent as ReactKeyboardEvent,useEffect,useMemo,useRef,useState } from "react";
import { Link,useLocation,useNavigate } from "react-router-dom";
import {
ExportDocManagerApiClient
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import {
numberValue,
readApiError,
readRouteSuccessMessage
} from "../../ui/formUtils.ts";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { listPageSizeOptions,loadListViewState,normalizeListPageSize,saveListViewState } from "../../ui/listViewState.ts";

import {
type MasterDataEntityConfig,
type MasterDataEntityKey,
type MasterDataRecord
} from "./masterDataTypes.ts";

import {
buildMasterDataDisplayName,
formatColumnValue
} from "./masterDataModel.ts";

import { HsCodeToolsPanel } from "./HsCodeToolsPanel.tsx";
import { masterDataConfigs } from "./masterDataConfigs.ts";

export function MasterDataListPage({
  client,
  config,
  canOperate = true,
  canManage = true,
}: {
  client: ExportDocManagerApiClient;
  config: MasterDataEntityConfig;
  canOperate?: boolean;
  canManage?: boolean;
}) {
  const listViewStateStorageKey = useMemo(() => buildMasterDataListViewStateStorageKey(config.key), [config.key]);
  const [initialListViewState] = useState(() => loadListViewState(listViewStateStorageKey));
  const [keyword, setKeyword] = useState(initialListViewState.keyword);
  const [committedKeyword, setCommittedKeyword] = useState(initialListViewState.keyword);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(initialListViewState.pageSize);
  const [listSuccessMessage, setListSuccessMessage] = useState<string | null>(null);
  const [selectedRecordIds, setSelectedRecordIds] = useState<Set<number>>(() => new Set());
  const skipNextListViewStateSaveRef = useRef(false);
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const routeSuccessMessage = readRouteSuccessMessage(location.state);
  const successMessage = listSuccessMessage ?? routeSuccessMessage;

  useEffect(() => {
    const nextViewState = loadListViewState(listViewStateStorageKey);
    skipNextListViewStateSaveRef.current = true;
    setKeyword(nextViewState.keyword);
    setCommittedKeyword(nextViewState.keyword);
    setPageNumber(1);
    setPageSize(nextViewState.pageSize);
    setListSuccessMessage(null);
    setSelectedRecordIds(new Set());
  }, [config.key, listViewStateStorageKey]);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      const nextKeyword = keyword.trim();
      if (nextKeyword !== committedKeyword) {
        setCommittedKeyword(nextKeyword);
        setPageNumber(1);
        setListSuccessMessage(null);
        setSelectedRecordIds(new Set());
      }
    }, 350);

    return () => window.clearTimeout(timer);
  }, [committedKeyword, keyword]);

  const masterDataQuery = useQuery({
    queryKey: queryKeys.masterDataList(config.key, pageNumber, pageSize, committedKeyword.trim()),
    queryFn: () =>
      config.list(client, {
        keyword: committedKeyword.trim(),
        pageNumber,
        pageSize,
      }),
    placeholderData: keepPreviousData,
  });

  useEffect(() => {
    setSelectedRecordIds(new Set());
  }, [committedKeyword, pageNumber, pageSize]);

  useEffect(() => {
    if (skipNextListViewStateSaveRef.current) {
      skipNextListViewStateSaveRef.current = false;
      return;
    }

    saveListViewState(listViewStateStorageKey, {
      keyword: committedKeyword,
      pageSize,
    });
  }, [committedKeyword, listViewStateStorageKey, pageSize]);

  useEffect(() => {
    if (masterDataQuery.data && masterDataQuery.data.pageNumber !== pageNumber) {
      setPageNumber(masterDataQuery.data.pageNumber);
    }
  }, [masterDataQuery.data, pageNumber]);

  function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setCommittedKeyword(keyword.trim());
    setPageNumber(1);
    setListSuccessMessage(null);
  }

  function handlePageSizeChange(nextPageSize: number) {
    setPageSize(normalizeListPageSize(nextPageSize));
    setPageNumber(1);
    setListSuccessMessage(null);
    setSelectedRecordIds(new Set());
  }

  const deleteMutation = useMutation({
    mutationFn: (record: MasterDataRecord) => config.delete(client, record, config.routeId(record)),
    onSuccess: async (response, deletedRecord) => {
      setListSuccessMessage(response.message || `${config.label}已删除。`);
      queryClient.removeQueries({ queryKey: queryKeys.masterDataRecord(config.key, config.routeId(deletedRecord)) });
      const invalidations = [queryClient.invalidateQueries({ queryKey: queryKeys.masterDataRoot(config.key) })];
      if (config.key === "customers" || config.key === "exporters") {
        invalidations.push(queryClient.invalidateQueries({ queryKey: queryKeys.invoiceParties() }));
      }

      await Promise.all(invalidations);
    },
    onError: () => {
      setListSuccessMessage(null);
    },
  });

  const batchDeleteHsCodesMutation = useMutation({
    mutationFn: (ids: number[]) =>
      client.deleteHsCodesBatch({
        body: { ids },
      }),
    onSuccess: async (response) => {
      setListSuccessMessage(response.message || "选中的HS编码已删除。");
      setSelectedRecordIds(new Set());
      await queryClient.invalidateQueries({ queryKey: queryKeys.masterDataRoot("hs-codes") });
      await masterDataQuery.refetch();
    },
    onError: () => {
      setListSuccessMessage(null);
    },
  });

  function handleDelete(record: MasterDataRecord) {
    if (!canManage || deleteMutation.isPending) {
      return;
    }

    const displayName = buildMasterDataDisplayName(config, record);
    if (!window.confirm(`确定删除当前${config.label} ${displayName} 吗？删除后无法在列表中继续查看。`)) {
      return;
    }

    setListSuccessMessage(null);
    deleteMutation.mutate(record);
  }

  function toggleSelectedRecord(record: MasterDataRecord, selected: boolean) {
    const id = numberValue(record.id);
    if (id <= 0) {
      return;
    }

    setSelectedRecordIds((current) => {
      const next = new Set(current);
      if (selected) {
        next.add(id);
      } else {
        next.delete(id);
      }

      return next;
    });
  }

  function toggleAllCurrentPageRecords(selected: boolean) {
    const currentRows = page?.items ?? [];
    setSelectedRecordIds((current) => {
      const next = new Set(current);
      for (const record of currentRows) {
        const id = numberValue(record.id);
        if (id <= 0) {
          continue;
        }

        if (selected) {
          next.add(id);
        } else {
          next.delete(id);
        }
      }

      return next;
    });
  }

  function handleBatchDeleteHsCodes() {
    if (!canManage || batchDeleteHsCodesMutation.isPending || selectedRecordIds.size === 0) {
      return;
    }

    const ids = Array.from(selectedRecordIds);
    if (!window.confirm(`确定删除选中的 ${ids.length} 条HS编码吗？删除后无法在本地库中继续查看。`)) {
      return;
    }

    setListSuccessMessage(null);
    batchDeleteHsCodesMutation.mutate(ids);
  }

  const page = masterDataQuery.data ?? null;
  const message = batchDeleteHsCodesMutation.isError
    ? readApiError(batchDeleteHsCodesMutation.error)
    : deleteMutation.isError
    ? readApiError(deleteMutation.error)
    : masterDataQuery.isError
      ? readApiError(masterDataQuery.error)
      : null;
  const isBusy = masterDataQuery.isFetching || deleteMutation.isPending || batchDeleteHsCodesMutation.isPending;

  async function handleLocalHsCodesChanged() {
    await queryClient.invalidateQueries({ queryKey: queryKeys.masterDataRoot("hs-codes") });
    await masterDataQuery.refetch();
  }

  return (
    <section className="work-surface master-data-surface" aria-label={config.listLabel}>
      <MasterDataTabs activeKey={config.key} />
      {!canOperate ? (
        <div className="permission-readonly-notice">
          当前权限模板仅允许查看主数据；新建、导入、联网保存和修改已禁用，删除需要管理权限。
        </div>
      ) : null}
      <div className="toolbar">
        <form className="search-form" onSubmit={handleSearch}>
          <Search size={17} aria-hidden="true" />
          <input
            aria-label={`搜索${config.label}`}
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            placeholder={config.searchPlaceholder}
          />
        </form>
        <div className="toolbar-actions">
          {config.key === "hs-codes" && canManage ? (
            <button
              className="command-button secondary danger"
              type="button"
              disabled={isBusy || selectedRecordIds.size === 0}
              onClick={handleBatchDeleteHsCodes}
            >
              <Trash2 size={17} aria-hidden="true" />
              <span>删除选中</span>
            </button>
          ) : null}
          <button
            className="icon-button"
            type="button"
            title="刷新"
            disabled={isBusy}
            onClick={() => void masterDataQuery.refetch()}
          >
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          {canOperate ? <button className="command-button" type="button" onClick={() => navigate(`/master-data/${config.key}/new`)}>
            <Plus size={17} aria-hidden="true" />
            <span>新建</span>
          </button> : null}
        </div>
      </div>

      {message ? <div className="alert">{message}</div> : null}
      {successMessage ? <div className="success-alert">{successMessage}</div> : null}

      {config.key === "hs-codes" ? (
        <HsCodeToolsPanel
          client={client}
          disabled={!canOperate || isBusy}
          keyword={keyword}
          onLocalDataChanged={handleLocalHsCodesChanged}
        />
      ) : null}

      <MasterDataTable
        config={config}
        data={page?.items ?? []}
        isBusy={isBusy}
        canOperate={canOperate}
        canManage={canManage}
        enableSelection={config.key === "hs-codes" && canManage}
        selectedIds={selectedRecordIds}
        onOpen={(record) => navigate(`/master-data/${config.key}/${config.routeId(record)}`)}
        onDelete={handleDelete}
        onToggleSelect={toggleSelectedRecord}
        onToggleSelectAll={toggleAllCurrentPageRecords}
      />

      <ListPaginationControls
        pageNumber={page?.pageNumber ?? pageNumber}
        totalPages={page?.totalPages ?? 1}
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

function MasterDataTabs({ activeKey }: { activeKey: MasterDataEntityKey }) {
  return (
    <nav className="master-data-tabs" aria-label="主数据分类">
      {masterDataConfigs.map((item) => (
        <Link
          className={item.key === activeKey ? "master-data-tab master-data-tab-active" : "master-data-tab"}
          key={item.key}
          to={`/master-data/${item.key}`}
        >
          {item.label}
        </Link>
      ))}
    </nav>
  );
}

function buildMasterDataListViewStateStorageKey(entityKey: MasterDataEntityKey) {
  return `export-doc-manager.master-data.${entityKey}.list-view-state.v1`;
}

function MasterDataTable({
  config,
  data,
  enableSelection = false,
  isBusy,
  canOperate,
  canManage,
  onDelete,
  onOpen,
  onToggleSelect,
  onToggleSelectAll,
  selectedIds = new Set<number>(),
}: {
  config: MasterDataEntityConfig;
  data: MasterDataRecord[];
  enableSelection?: boolean;
  isBusy: boolean;
  canOperate: boolean;
  canManage: boolean;
  onDelete: (record: MasterDataRecord) => void;
  onOpen: (record: MasterDataRecord) => void;
  onToggleSelect?: (record: MasterDataRecord, selected: boolean) => void;
  onToggleSelectAll?: (selected: boolean) => void;
  selectedIds?: Set<number>;
}) {
  function handleRowKeyDown(event: ReactKeyboardEvent<HTMLTableRowElement>, record: MasterDataRecord) {
    if (event.target !== event.currentTarget) {
      return;
    }

    if (enableSelection && event.ctrlKey && event.key.toLowerCase() === "a") {
      event.preventDefault();
      onToggleSelectAll?.(true);
      return;
    }

    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      onOpen(record);
      return;
    }

    if (canManage && event.key === "Delete") {
      event.preventDefault();
      onDelete(record);
    }
  }

  const selectableIds = data.map((record) => numberValue(record.id)).filter((id) => id > 0);
  const allCurrentPageSelected = selectableIds.length > 0 && selectableIds.every((id) => selectedIds.has(id));
  const tableColumnCount = config.columns.length + 1 + (enableSelection ? 1 : 0);

  return (
    <div className="table-frame" aria-busy={isBusy}>
      <table className="master-data-table">
        <thead>
          <tr>
            {enableSelection ? (
              <th className="selection-cell">
                <input
                  type="checkbox"
                  aria-label="选择当前页HS编码"
                  checked={allCurrentPageSelected}
                  disabled={isBusy || selectableIds.length === 0}
                  onChange={(event) => onToggleSelectAll?.(event.target.checked)}
                />
              </th>
            ) : null}
            {config.columns.map((column) => (
              <th className={column.className} key={column.name}>
                {column.label}
              </th>
            ))}
            <th className="row-actions-cell">操作</th>
          </tr>
        </thead>
        <tbody>
          {data.length === 0 ? (
            <tr>
              <td colSpan={tableColumnCount} className="empty-cell">
                {isBusy ? "加载中" : "暂无数据"}
              </td>
            </tr>
          ) : (
            data.map((record) => (
              <tr
                className="clickable-row"
                key={config.routeId(record)}
                tabIndex={0}
                onClick={() => onOpen(record)}
                onKeyDown={(event) => handleRowKeyDown(event, record)}
              >
                {enableSelection ? (
                  <td className="selection-cell" onClick={(event) => event.stopPropagation()}>
                    <input
                      type="checkbox"
                      aria-label={`选择HS编码 ${buildMasterDataDisplayName(config, record)}`}
                      checked={selectedIds.has(numberValue(record.id))}
                      disabled={isBusy || numberValue(record.id) <= 0}
                      onChange={(event) => onToggleSelect?.(record, event.target.checked)}
                    />
                  </td>
                ) : null}
                {config.columns.map((column, columnIndex) => (
                  <td className={`${column.className ?? ""} ${columnIndex === 0 ? "strong-cell" : ""}`.trim()} key={column.name}>
                    {formatColumnValue(column, record)}
                  </td>
                ))}
                <td className="row-actions-cell">
                  <button
                    className="icon-button compact-icon-button"
                    type="button"
                    title={canOperate ? "编辑" : "查看"}
                    aria-label={`${canOperate ? "编辑" : "查看"}${config.label} ${buildMasterDataDisplayName(config, record)}`}
                    disabled={isBusy}
                    onClick={(event) => {
                      event.stopPropagation();
                      onOpen(record);
                    }}
                  >
                    <Edit3 size={15} aria-hidden="true" />
                  </button>
                  {canManage ? <button
                    className="icon-button compact-icon-button danger-icon"
                    type="button"
                    title="删除"
                    aria-label={`删除${config.label} ${buildMasterDataDisplayName(config, record)}`}
                    disabled={isBusy}
                    onClick={(event) => {
                      event.stopPropagation();
                      onDelete(record);
                    }}
                  >
                    <Trash2 size={15} aria-hidden="true" />
                  </button> : null}
                </td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}

