import { ListWorkspace } from "../../ui/layout/Workspaces.tsx";
import { FilterBar } from "../../ui/primitives/Field.tsx";
import { Edit3, Eye, Plus, RefreshCw, Search, Trash2 } from "lucide-react";
import { KeyboardEvent as ReactKeyboardEvent } from "react";
import { Link } from "react-router-dom";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { numberValue } from "../../ui/formUtils.ts";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { PageHeader } from "../../ui/primitives/PageHeader.tsx";
import { ConfirmationDialog } from "../../ui/ConfirmationDialog.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { listPageSizeOptions } from "../../ui/listViewState.ts";
import { type MasterDataEntityConfig, type MasterDataEntityKey, type MasterDataRecord } from "./masterDataTypes.ts";
import { buildMasterDataDisplayName, formatColumnValue } from "./masterDataModel.ts";
import { masterDataConfigs } from "./masterDataConfigs.ts";
import { HsKnowledgeNavigation } from "./HsKnowledgeNavigation.tsx";
import { HsCodeObsoleteAction } from "./HsCodeObsoleteAction.tsx";
import { useMasterDataListWorkspace } from "./hooks/useMasterDataListWorkspace.ts";

export function MasterDataListPage({
  client,
  config,
  canOperate,
  canManage,
  canViewMasterData,
}: {
  client: ExportDocManagerApiClient;
  config: MasterDataEntityConfig;
  canOperate: boolean;
  canManage: boolean;
  canViewMasterData: boolean;
}) {
  const {
    keyword, setKeyword, pageNumber, setPageNumber, pageSize,
    setListSuccessMessage, selectedRecords, setSelectedRecords, selectedRecordIds, pendingDeleteRecord,
    setPendingDeleteRecord, batchDeleteDialogOpen, setBatchDeleteDialogOpen, navigate, queryClient,
    successMessage, masterDataQuery, handleSearch, handlePageSizeChange, deleteMutation,
    batchDeleteHsCodesMutation, handleDelete, toggleSelectedRecord, toggleAllCurrentPageRecords, handleBatchDeleteHsCodes,
    page, message, isBusy,
  } = useMasterDataListWorkspace({ client, config, canOperate, canManage, canViewMasterData });

  return (
    <ListWorkspace className="master-data-surface" aria-label={config.listLabel}>
      {config.key === "hs-codes" ? <HsKnowledgeNavigation activeSection="catalog" canManage={canManage} />
        : <MasterDataTabs activeKey={config.key} canViewMasterData={canViewMasterData} />}
      {!canOperate ? (
        <PermissionNotice>
          当前权限模板仅允许查看主数据；新建、导入、联网保存和修改已禁用，删除需要管理权限。
        </PermissionNotice>
      ) : null}
      <PageHeader title={config.listLabel} count={page?.totalCount}
        description={`查找和维护${config.label}，打开记录查看详情或编辑。`}
        actions={<>
          <button className="icon-button" type="button" title="刷新" aria-label="刷新" disabled={isBusy} onClick={() => void masterDataQuery.refetch()}><RefreshCw size={18} aria-hidden="true" /></button>
          {canOperate && <button className="command-button" type="button" onClick={() => navigate(`/master-data/${config.key}/new`)}><Plus size={17} aria-hidden="true" />新建</button>}
        </>}
        selection={config.key === "hs-codes" && canManage ? <>
          <span>已选 {selectedRecordIds.size} 条编码</span>
          <HsCodeObsoleteAction client={client} records={Array.from(selectedRecords.values())} disabled={isBusy} onChanged={async () => {
            setSelectedRecords(new Map());
            await queryClient.invalidateQueries({ queryKey: queryKeys.masterDataRoot("hs-codes") });
          }} />
            <button
              className="command-button secondary danger"
              type="button"
              disabled={isBusy || selectedRecordIds.size === 0}
              onClick={handleBatchDeleteHsCodes}
            >
              <Trash2 size={17} aria-hidden="true" />
              <span>删除选中</span>
            </button>
        </> : null}>
        <FilterBar className="list-workspace-filters" label={`筛选${config.label}`} onSubmit={handleSearch}>
          <div className="search-form"><Search size={17} aria-hidden="true" />
            <input aria-label={`搜索${config.label}`} value={keyword} onChange={(event) => setKeyword(event.target.value)} placeholder={config.searchPlaceholder} />
          </div>
          <button type="submit" className="command-button secondary">搜索</button>
        </FilterBar>
      </PageHeader>

      {message ? <InlineNotice tone="error" title="操作未完成">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}

      <MasterDataTable
        config={config}
        data={page?.items ?? []}
        isBusy={isBusy}
        hasError={Boolean(message)}
        canOperate={canOperate}
        canManage={canManage}
        enableSelection={config.key === "hs-codes" && canManage}
        selectedIds={selectedRecordIds}
        onOpen={(record) => navigate(`/master-data/${config.key}/${config.routeId(record)}`)}
        onDelete={handleDelete}
        onToggleSelect={toggleSelectedRecord}
        onToggleSelectAll={toggleAllCurrentPageRecords}
      />

      <Pagination
        pageNumber={page?.pageNumber ?? pageNumber}
        totalPages={page?.totalPages ?? 1}
        totalCount={page?.totalCount ?? 0}
        pageSize={pageSize}
        pageSizeOptions={listPageSizeOptions}
        isBusy={isBusy}
        onPageChange={setPageNumber}
        onPageSizeChange={handlePageSizeChange}
      />
      {pendingDeleteRecord ? (
        <ConfirmationDialog
          title={`删除${config.label}`}
          description={`确定删除“${buildMasterDataDisplayName(config, pendingDeleteRecord)}”吗？`}
          details={config.key === "hs-codes" ? ["只删除目录记录，历史单据、商品、申报实例及替代关系保留。", "使用读取时版本，记录变化后拒绝删除；年度淘汰可先确认作废再按保留期清理。"] : ["删除后无法在列表中继续查看。", "如该资料已被业务单据引用，系统会拒绝删除并说明原因。"]}
          confirmLabel="确认删除"
          isBusy={deleteMutation.isPending}
          onCancel={() => setPendingDeleteRecord(null)}
          onConfirm={() => {
            setListSuccessMessage(null);
            deleteMutation.mutate(pendingDeleteRecord);
          }}
        />
      ) : null}
      {batchDeleteDialogOpen ? (
        <ConfirmationDialog
          title="批量删除 HS 编码"
          description={`确定删除选中的 ${selectedRecordIds.size} 条 HS 编码吗？`}
          details={["只删除选中的目录记录，历史单据、商品、申报实例及替代关系保留。", "使用选择时版本，任一编码已变化则整批回滚。"]}
          confirmLabel={`删除 ${selectedRecordIds.size} 条`}
          isBusy={batchDeleteHsCodesMutation.isPending}
          onCancel={() => setBatchDeleteDialogOpen(false)}
          onConfirm={() => {
            setListSuccessMessage(null);
            batchDeleteHsCodesMutation.mutate(Array.from(selectedRecords.values()));
          }}
        />
      ) : null}
    </ListWorkspace>
  );
}

function MasterDataTabs({
  activeKey,
  canViewMasterData,
}: {
  activeKey: MasterDataEntityKey;
  canViewMasterData: boolean;
}) {
  const visibleConfigs = masterDataConfigs.filter((item) =>
    item.key !== "hs-codes" && canViewMasterData);
  return (
    <nav className="master-data-tabs" aria-label="主数据分类">
      {visibleConfigs.map((item) => (
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


function MasterDataTable({
  config,
  data,
  enableSelection = false,
  isBusy,
  hasError,
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
  hasError: boolean;
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
    <ResponsiveTableFrame label={`${config.label}列表`} mobileLayout="cards" busy={isBusy}>
      <table className="master-data-table responsive-data-table">
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
          {data.length === 0 && !hasError ? (
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
                  <td className="selection-cell" data-label="选择" onClick={(event) => event.stopPropagation()}>
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
                  <td
                    className={`${column.className ?? ""} ${columnIndex === 0 ? "strong-cell" : ""}`.trim()}
                    data-label={column.label}
                    data-table-priority={columnIndex >= 3 ? "secondary" : "primary"}
                    key={column.name}
                  >
                    {formatColumnValue(column, record)}
                  </td>
                ))}
                <td className="row-actions-cell" data-label="操作">
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
                    {canOperate ? <Edit3 size={15} aria-hidden="true" /> : <Eye size={15} aria-hidden="true" />}
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
    </ResponsiveTableFrame>
  );
}
