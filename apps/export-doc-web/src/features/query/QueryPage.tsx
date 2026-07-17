import { FocusEvent, FormEvent, KeyboardEvent, useEffect, useMemo, useRef, useState } from "react";
import { keepPreviousData, useMutation, useQuery } from "@tanstack/react-query";
import { Download, FolderOpen, RefreshCw, Search } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { ApiQueryInvoiceRowDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, selectSaveExcelPath } from "../../desktop/desktopBridge.ts";
import { DesktopIconButton, readDesktopError, renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { readStoredJson, writeStoredJson } from "../../ui/browserStorage.ts";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { formatAmount, formatPlainNumber, readApiError } from "../../ui/formUtils.ts";
import { readDefaultExportDirectory } from "../settings/settingsPaths.ts";

const defaultPageSize = 50;
const pageSizeOptions = [20, 50, 100, 200] as const;
const queryViewStateStorageKey = "exportDocManager.queryViewState.v1";
const invoiceTypeOptions = ["", "实际数据", "报关数据"];
const transportModeOptions = ["", "BY SEA", "BY AIR", "BY TRAIN", "BY DHL", "BY FedEx"];

type QueryFilters = {
  startDate: string;
  endDate: string;
  customerId: string;
  exporterId: string;
  keyword: string;
  invoiceType: string;
  transportMode: string;
};

export function QueryPage({ client }: { client: ExportDocManagerApiClient }) {
  const queryPermission = useModulePermission("document.query");
  const navigate = useNavigate();
  const keywordInputRef = useRef<HTMLInputElement | null>(null);
  const isDesktop = isDesktopBridgeAvailable();
  const [initialViewState] = useState(() => loadQueryViewState());
  const [filters, setFilters] = useState<QueryFilters>(() => initialViewState.filters);
  const [committedFilters, setCommittedFilters] = useState<QueryFilters>(() => initialViewState.filters);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(() => initialViewState.pageSize);
  const [exportPath, setExportPath] = useState("");
  const [actionMessage, setActionMessage] = useState<{ kind: "success" | "error"; text: string } | null>(null);

  const partiesQuery = useQuery({
    queryKey: queryKeys.invoiceParties(),
    queryFn: async () => {
      const [customers, exporters] = await Promise.all([
        client.listCustomers({}),
        client.listExporters({}),
      ]);
      return { customers, exporters };
    },
    staleTime: 5 * 60 * 1000,
  });

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: () => client.getSettings(),
    staleTime: 5 * 60 * 1000,
  });

  const invoiceQuery = useQuery({
    queryKey: queryKeys.queryInvoices(pageNumber, pageSize, committedFilters),
    queryFn: () =>
      client.listQueriedInvoices({
        ...toApiFilters(committedFilters),
        pageNumber,
        pageSize,
      }),
    placeholderData: keepPreviousData,
  });

  useEffect(() => {
    if (invoiceQuery.data && invoiceQuery.data.pageNumber !== pageNumber) {
      setPageNumber(invoiceQuery.data.pageNumber);
    }
  }, [invoiceQuery.data, pageNumber]);

  useEffect(() => {
    saveQueryViewState(committedFilters, pageSize);
  }, [committedFilters, pageSize]);

  useEffect(() => {
    function handlePageKeyDown(event: globalThis.KeyboardEvent) {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "f") {
        event.preventDefault();
        keywordInputRef.current?.focus();
        keywordInputRef.current?.select();
        return;
      }

      if (event.key === "F5") {
        event.preventDefault();
        setActionMessage(null);
        void invoiceQuery.refetch();
      }
    }

    window.addEventListener("keydown", handlePageKeyDown);
    return () => window.removeEventListener("keydown", handlePageKeyDown);
  }, [invoiceQuery]);

  const exportMutation = useMutation({
    mutationFn: async () => {
      if (isDesktop) {
        const response = await client.saveQueriedInvoicesToPath({
          body: {
            ...toApiFilters(committedFilters),
            destinationPath: exportPath.trim(),
          },
        });
        return { mode: "desktop" as const, response };
      }

      const blob = await client.downloadQueriedInvoices({
        body: toApiFilters(committedFilters),
      });
      downloadBlob(blob, buildQueryExportFileName());
      return { mode: "browser" as const };
    },
    onSuccess: (result) => {
      if (result.mode === "browser") {
        setActionMessage({ kind: "success", text: "查询结果 Excel 已交给浏览器下载。" });
        return;
      }

      const response = result.response;
      setActionMessage({
        kind: "success",
        text: response.destinationPath
          ? `${response.message} ${response.exportedCount} 条，${response.destinationPath}`
          : `${response.message} ${response.exportedCount} 条`,
      });
    },
    onError: (error) => setActionMessage({ kind: "error", text: readApiError(error) }),
  });

  const page = invoiceQuery.data ?? null;
  const rows = page?.items ?? [];
  const isBusy = invoiceQuery.isFetching;
  const isActionBusy = exportMutation.isPending;
  const message = invoiceQuery.isError ? readApiError(invoiceQuery.error) : null;
  const canExport = queryPermission.canOperate && (!isDesktop || Boolean(exportPath.trim())) && !isActionBusy;

  const customerOptions = useMemo(
    () => (partiesQuery.data?.customers ?? []).slice().sort((left, right) => left.displayName.localeCompare(right.displayName)),
    [partiesQuery.data?.customers],
  );
  const exporterOptions = useMemo(
    () =>
      (partiesQuery.data?.exporters ?? [])
        .slice()
        .sort((left, right) => left.exporterNameEN.localeCompare(right.exporterNameEN)),
    [partiesQuery.data?.exporters],
  );
  const defaultExportDirectory = readDefaultExportDirectory(settingsQuery.data?.settings);

  function updateFilter(key: keyof QueryFilters, value: string) {
    setFilters((current) => ({ ...current, [key]: value }));
  }

  function runSearch() {
    setCommittedFilters(normalizeFilters(filters));
    setActionMessage(null);
    setPageNumber(1);
  }

  function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    runSearch();
  }

  function handleQueryFormKeyDown(event: KeyboardEvent<HTMLFormElement>) {
    const shouldSearch =
      event.key === "Enter" &&
      !event.ctrlKey &&
      !event.metaKey &&
      !event.altKey &&
      event.target instanceof HTMLElement &&
      event.target.getAttribute("data-query-filter") === "keyword";

    handleEnterAsTabFormKeyDown(event);

    if (shouldSearch) {
      runSearch();
    }
  }

  function handleTextInputFocus(event: FocusEvent<HTMLInputElement>) {
    if (event.currentTarget.type !== "date") {
      event.currentTarget.select();
    }
  }

  function handlePageSizeChange(value: number) {
    setPageSize(normalizePageSize(value));
    setActionMessage(null);
    setPageNumber(1);
  }

  async function handleSelectExportPath() {
    try {
      const selected = await selectSaveExcelPath(buildQueryExportFileName(), defaultExportDirectory);
      if (selected) {
        setExportPath(selected);
        setActionMessage(null);
      }
    } catch (error) {
      setActionMessage({ kind: "error", text: readDesktopError(error) });
    }
  }

  function handleExport() {
    if (!canExport) {
      return;
    }

    setActionMessage(null);
    exportMutation.mutate();
  }

  return (
    <section className="work-surface query-surface" aria-label="单据查询">
      <form className="toolbar query-toolbar" onSubmit={handleSearch} onKeyDown={handleQueryFormKeyDown}>
        <div className="filter-bar query-filter-bar">
          <label className="inline-filter query-date-filter">
            <span>开始</span>
            <input
              type="date"
              data-query-filter="startDate"
              value={filters.startDate}
              onChange={(event) => updateFilter("startDate", event.target.value)}
            />
          </label>
          <label className="inline-filter query-date-filter">
            <span>结束</span>
            <input
              type="date"
              data-query-filter="endDate"
              value={filters.endDate}
              onChange={(event) => updateFilter("endDate", event.target.value)}
            />
          </label>
          <label className="inline-filter query-party-filter">
            <span>客户</span>
            <select
              data-query-filter="customerId"
              value={filters.customerId}
              onChange={(event) => updateFilter("customerId", event.target.value)}
            >
              <option value="0">全部</option>
              {customerOptions.map((customer) => (
                <option key={customer.id} value={String(customer.id)}>
                  {customer.displayName || customer.customerNameEN}
                </option>
              ))}
            </select>
          </label>
          <label className="inline-filter query-party-filter">
            <span>出口商</span>
            <select
              data-query-filter="exporterId"
              value={filters.exporterId}
              onChange={(event) => updateFilter("exporterId", event.target.value)}
            >
              <option value="0">全部</option>
              {exporterOptions.map((exporter) => (
                <option key={exporter.id} value={String(exporter.id)}>
                  {exporter.exporterNameEN || exporter.exporterNameCN}
                </option>
              ))}
            </select>
          </label>
          <label className="inline-filter">
            <span>类型</span>
            <select
              data-query-filter="invoiceType"
              value={filters.invoiceType}
              onChange={(event) => updateFilter("invoiceType", event.target.value)}
            >
              <option value="">全部</option>
              {invoiceTypeOptions.filter(Boolean).map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </label>
          <label className="inline-filter">
            <span>运输</span>
            <select
              data-query-filter="transportMode"
              value={filters.transportMode}
              onChange={(event) => updateFilter("transportMode", event.target.value)}
            >
              <option value="">全部</option>
              {transportModeOptions.filter(Boolean).map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </label>
          <label className="inline-filter query-text-filter query-keyword-filter">
            <span>关键字</span>
            <input
              ref={keywordInputRef}
              data-query-filter="keyword"
              value={filters.keyword}
              onFocus={handleTextInputFocus}
              onChange={(event) => updateFilter("keyword", event.target.value)}
            />
          </label>
        </div>
        <div className="toolbar-actions">
          <button className="icon-button" type="button" title="刷新" disabled={isBusy} onClick={() => void invoiceQuery.refetch()}>
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          <button className="icon-button solid" type="submit" title="查询" disabled={isBusy}>
            <Search size={18} aria-hidden="true" />
          </button>
        </div>
      </form>

      {queryPermission.canOperate ? <section className="form-section query-export-panel" aria-label="查询结果导出">
        <div className="section-header">
          <div>
            <h2>导出</h2>
            <span>{page ? `${page.totalCount} 条当前结果` : "等待查询"}</span>
          </div>
        </div>
        <div className="query-export-grid">
          {isDesktop ? <label className="inline-filter query-export-path-filter">
            <span>输出路径</span>
            <input
              value={exportPath}
              placeholder="QueryResults.xlsx"
              disabled={isActionBusy}
              onChange={(event) => setExportPath(event.target.value)}
            />
          </label> : <div className="field-help">文件将保存到浏览器默认下载目录。</div>}
          <div className="query-inline-actions">
            {isDesktop ? (
              <DesktopIconButton title="选择 Excel 保存位置" disabled={isActionBusy} onClick={handleSelectExportPath}>
                <FolderOpen size={16} aria-hidden="true" />
              </DesktopIconButton>
            ) : null}
            {isDesktop ? renderOpenPathAction(exportPath, "打开导出文件", (text) => setActionMessage({ kind: "error", text })) : null}
            <button className="icon-button solid" type="button" title="导出 Excel" disabled={!canExport} onClick={handleExport}>
              <Download size={18} aria-hidden="true" />
            </button>
          </div>
        </div>
      </section> : null}

      {message ? <div className="alert">{message}</div> : null}
      {partiesQuery.isError ? <div className="alert">{readApiError(partiesQuery.error)}</div> : null}
      {actionMessage ? <div className={actionMessage.kind === "success" ? "success-alert" : "alert"}>{actionMessage.text}</div> : null}

      <QueryResultTable data={rows} isBusy={isBusy} onOpen={(invoiceId) => navigate(`/invoices/${invoiceId}`)} />

      <ListPaginationControls
        pageNumber={page?.pageNumber ?? pageNumber}
        totalPages={page?.totalPages ?? 1}
        totalCount={page?.totalCount ?? 0}
        pageSize={pageSize}
        pageSizeOptions={pageSizeOptions}
        isBusy={isBusy}
        onPageChange={setPageNumber}
        onPageSizeChange={handlePageSizeChange}
      />
    </section>
  );
}

function QueryResultTable({
  data,
  isBusy,
  onOpen,
}: {
  data: ApiQueryInvoiceRowDto[];
  isBusy: boolean;
  onOpen: (invoiceId: number) => void;
}) {
  function handleRowKeyDown(event: KeyboardEvent<HTMLTableRowElement>, invoiceId: number) {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      onOpen(invoiceId);
    }
  }

  return (
    <div className="table-frame query-table-frame" aria-busy={isBusy}>
      <table className="query-table">
        <thead>
          <tr>
            <th>发票号</th>
            <th>日期</th>
            <th>合同号</th>
            <th>客户</th>
            <th>出口商</th>
            <th>目的国</th>
            <th>贸易条款</th>
            <th>船期/航期</th>
            <th>运输方式</th>
            <th className="amount-cell">箱数</th>
            <th className="amount-cell">数量</th>
            <th className="amount-cell">金额</th>
            <th>类型</th>
          </tr>
        </thead>
        <tbody>
          {data.length === 0 ? (
            <tr>
              <td colSpan={13} className="empty-cell">
                {isBusy ? "加载中" : "暂无查询结果"}
              </td>
            </tr>
          ) : (
            data.map((row) => (
              <tr
                className="clickable-row"
                key={row.id}
                tabIndex={0}
                onClick={() => onOpen(row.id)}
                onKeyDown={(event) => handleRowKeyDown(event, row.id)}
              >
                <td className="strong-cell">{row.invoiceNo || "-"}</td>
                <td>{row.invoiceDate || "-"}</td>
                <td>{row.contractNo || "-"}</td>
                <td>{row.customerName || "-"}</td>
                <td>{row.exporterName || "-"}</td>
                <td>{row.destinationCountry || "-"}</td>
                <td>{row.tradeTerms || "-"}</td>
                <td>{row.shipmentDate || "-"}</td>
                <td>{row.transportMode || "-"}</td>
                <td className="amount-cell">{formatPlainNumber(row.totalCartons)}</td>
                <td className="amount-cell">{formatPlainNumber(row.totalQuantity)}</td>
                <td className="amount-cell">{formatAmount(row.totalAmount, row.currency)}</td>
                <td>
                  <span className="status-pill">{row.type || "-"}</span>
                </td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}

function createDefaultFilters(): QueryFilters {
  const now = new Date();
  const start = new Date(now.getFullYear(), now.getMonth(), 1);
  return {
    startDate: toDateInputValue(start),
    endDate: toDateInputValue(now),
    customerId: "0",
    exporterId: "0",
    keyword: "",
    invoiceType: "",
    transportMode: "",
  };
}

function loadQueryViewState(): { filters: QueryFilters; pageSize: number } {
  const defaults = createDefaultFilters();
  const parsed = readStoredJson<{ filters?: unknown; pageSize?: unknown }>(queryViewStateStorageKey);
  return {
    filters: normalizeFilters(readStoredFilters(parsed?.filters, defaults)),
    pageSize: normalizePageSize(parsed?.pageSize),
  };
}

function saveQueryViewState(filters: QueryFilters, pageSize: number) {
  writeStoredJson(queryViewStateStorageKey, {
    filters: normalizeFilters(filters),
    pageSize: normalizePageSize(pageSize),
  });
}

function readStoredFilters(value: unknown, defaults: QueryFilters): QueryFilters {
  if (!value || typeof value !== "object") {
    return defaults;
  }

  const stored = value as Partial<Record<keyof QueryFilters, unknown>>;
  const legacyStored = value as Record<string, unknown>;
  return {
    startDate: readStoredString(stored.startDate, defaults.startDate),
    endDate: readStoredString(stored.endDate, defaults.endDate),
    customerId: readStoredString(stored.customerId, defaults.customerId),
    exporterId: readStoredString(stored.exporterId, defaults.exporterId),
    keyword: mergeQueryKeywords(
      readStoredString(stored.keyword, defaults.keyword),
      readStoredString(legacyStored.contractNo, ""),
      readStoredString(legacyStored.styleName, ""),
      readStoredString(legacyStored.styleNo, ""),
    ),
    invoiceType: readStoredString(stored.invoiceType, defaults.invoiceType),
    transportMode: readStoredString(stored.transportMode, defaults.transportMode),
  };
}

function readStoredString(value: unknown, fallback: string) {
  return typeof value === "string" ? value : fallback;
}

function normalizePageSize(value: unknown) {
  const numericValue = typeof value === "number" ? value : Number(value);
  return pageSizeOptions.includes(numericValue as (typeof pageSizeOptions)[number]) ? numericValue : defaultPageSize;
}

function normalizeFilters(filters: QueryFilters): QueryFilters {
  return {
    startDate: filters.startDate,
    endDate: filters.endDate,
    customerId: filters.customerId,
    exporterId: filters.exporterId,
    keyword: filters.keyword.trim(),
    invoiceType: filters.invoiceType.trim(),
    transportMode: filters.transportMode.trim(),
  };
}

function toApiFilters(filters: QueryFilters) {
  const normalized = normalizeFilters(filters);
  const customerId = Number(normalized.customerId);
  const exporterId = Number(normalized.exporterId);
  return {
    startDate: normalized.startDate ? `${normalized.startDate}T00:00:00` : undefined,
    endDate: normalized.endDate ? `${normalized.endDate}T23:59:59` : undefined,
    customerId: Number.isFinite(customerId) && customerId > 0 ? customerId : undefined,
    exporterId: Number.isFinite(exporterId) && exporterId > 0 ? exporterId : undefined,
    keyword: normalized.keyword || undefined,
    invoiceType: normalized.invoiceType || undefined,
    transportMode: normalized.transportMode || undefined,
  };
}

function mergeQueryKeywords(...values: string[]) {
  const keywords: string[] = [];
  const seen = new Set<string>();

  for (const value of values) {
    const keyword = value.trim();
    const normalized = keyword.toUpperCase();
    if (!keyword || seen.has(normalized)) {
      continue;
    }

    keywords.push(keyword);
    seen.add(normalized);
  }

  return keywords.join(" ");
}

function toDateInputValue(value: Date) {
  const pad = (number: number) => String(number).padStart(2, "0");
  return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}`;
}

function buildQueryExportFileName() {
  const now = new Date();
  const pad = (number: number) => String(number).padStart(2, "0");
  return `QueryResults_${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}.xlsx`;
}
