import { keepPreviousData,useQuery } from "@tanstack/react-query";
import { RefreshCw,Search } from "lucide-react";
import { FormEvent,useEffect,useState } from "react";
import {
ExportDocManagerApiClient
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { listPageSizeOptions,normalizeListPageSize } from "../../ui/listViewState.ts";

import {
businessTypeOptions,
collaborationStatusOptions,
loadSingleWindowCollaborationViewState,
saveSingleWindowCollaborationViewState
} from "./singleWindowOperationCenterModel.ts";

import { SingleWindowTabs } from "./SingleWindowNavigation.tsx";
import { FilterSelect } from "./SingleWindowOperationCenterList.tsx";
import { TicketTable,WorkstationTable } from "./SingleWindowOperationCenterTables.tsx";

export function SingleWindowCollaborationPage({ client }: { client: ExportDocManagerApiClient }) {
  const permission = useModulePermission("document.single-window");
  const [initialListViewState] = useState(() => loadSingleWindowCollaborationViewState());
  const [keyword, setKeyword] = useState(initialListViewState.keyword);
  const [committedKeyword, setCommittedKeyword] = useState(initialListViewState.keyword);
  const [businessType, setBusinessType] = useState(initialListViewState.businessType);
  const [status, setStatus] = useState(initialListViewState.status);
  const [includeDisabledWorkstations, setIncludeDisabledWorkstations] = useState(initialListViewState.includeDisabledWorkstations);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(initialListViewState.pageSize);

  const collaborationQuery = useQuery({
    queryKey: queryKeys.singleWindowCollaboration(
      pageNumber,
      pageSize,
      committedKeyword.trim(),
      businessType,
      status,
      includeDisabledWorkstations,
    ),
    queryFn: () =>
      client.listSingleWindowCollaboration({
        businessType: businessType || undefined,
        status: status || undefined,
        keyword: committedKeyword.trim() || undefined,
        pageNumber,
        pageSize,
        includeDisabledWorkstations,
      }),
    placeholderData: keepPreviousData,
  });

  useEffect(() => {
    if (collaborationQuery.data && collaborationQuery.data.pageNumber !== pageNumber) {
      setPageNumber(collaborationQuery.data.pageNumber);
    }
  }, [collaborationQuery.data, pageNumber]);

  useEffect(() => {
    saveSingleWindowCollaborationViewState({
      keyword: committedKeyword,
      businessType,
      status,
      includeDisabledWorkstations,
      pageSize,
    });
  }, [businessType, committedKeyword, includeDisabledWorkstations, pageSize, status]);

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

  function changeIncludeDisabledWorkstations(value: boolean) {
    setIncludeDisabledWorkstations(value);
    setPageNumber(1);
  }

  function handlePageSizeChange(nextPageSize: number) {
    setPageSize(normalizeListPageSize(nextPageSize));
    setPageNumber(1);
  }

  const page = collaborationQuery.data ?? null;
  const tickets = page?.tickets ?? [];
  const workstations = page?.workstations ?? [];
  const totalPages = Math.max(Math.ceil((page?.totalTicketCount ?? 0) / (page?.pageSize || pageSize)), 1);
  const message = collaborationQuery.isError ? readApiError(collaborationQuery.error) : null;
  const isBusy = collaborationQuery.isFetching;

  return (
    <section className="work-surface single-window-surface single-window-board-surface" aria-label="单一窗口协同看板">
      <SingleWindowTabs activeKey="collaboration" />

      <div className="toolbar single-window-toolbar">
        <form className="search-form" onSubmit={handleSearch}>
          <Search size={17} aria-hidden="true" />
          <input
            aria-label="搜索单一窗口工单"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            placeholder="发票号、工单号、操作员、异常"
          />
        </form>
        <div className="filter-bar">
          <FilterSelect
            label="业务"
            value={businessType}
            options={businessTypeOptions}
            onChange={changeBusinessType}
          />
          <FilterSelect label="状态" value={status} options={collaborationStatusOptions} onChange={changeStatus} />
          <label className="inline-check">
            <input
              type="checkbox"
              checked={includeDisabledWorkstations}
              onChange={(event) => changeIncludeDisabledWorkstations(event.target.checked)}
            />
            <span>含停用工位</span>
          </label>
        </div>
        <div className="toolbar-actions">
          <button
            className="icon-button"
            type="button"
            title="刷新" aria-label="刷新"
            disabled={isBusy}
            onClick={() => void collaborationQuery.refetch()}
          >
            <RefreshCw size={18} aria-hidden="true" />
          </button>
        </div>
      </div>

      {message ? <InlineNotice tone="error" title="协同看板加载失败">{message}</InlineNotice> : null}
      {!permission.canOperate ? (
        <PermissionNotice>
          当前权限仅允许查询协同工单和工位状态；协作处理与状态推进已禁用。
        </PermissionNotice>
      ) : null}

      <div className="single-window-board-grid">
        <section className="board-panel" aria-label="协同工单">
          <div className="board-panel-header">
            <h2>工单</h2>
            <span>{page ? `${page.totalTicketCount} 条` : "0 条"}</span>
          </div>
          <TicketTable data={tickets} isBusy={isBusy} />
        </section>
        <section className="board-panel" aria-label="操作工位">
          <div className="board-panel-header">
            <h2>工位</h2>
            <span>{workstations.length} 台</span>
          </div>
          <WorkstationTable data={workstations} isBusy={isBusy} />
        </section>
      </div>

      <ListPaginationControls
        pageNumber={page?.pageNumber ?? pageNumber}
        totalPages={totalPages}
        totalCount={page?.totalTicketCount ?? 0}
        pageSize={pageSize}
        pageSizeOptions={listPageSizeOptions}
        isBusy={isBusy}
        onPageChange={setPageNumber}
        onPageSizeChange={handlePageSizeChange}
      />
    </section>
  );
}
