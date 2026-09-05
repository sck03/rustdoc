import { type FormEvent, useEffect, useState } from "react";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import { Play, RefreshCw, Search, Trash2, X } from "lucide-react";
import { useSearchParams } from "react-router-dom";
import { type BackgroundJobSnapshot, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useWorkspaceDeviceProfile } from "../../app/workspaceDevice.ts";
import { isDesktopBridgeAvailable } from "../../desktop/desktopBridge.ts";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { WorkspaceDeviceNotice } from "../../ui/WorkspaceDeviceNotice.tsx";
import { listPageSizeOptions, loadListViewState, normalizeListPageSize, saveListViewState } from "../../ui/listViewState.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { readDefaultReportTemplatePath, resolveReportTemplatePath } from "../reports/reportTemplateSelectionModel.ts";
import { readDefaultExportDirectory } from "../settings/settingsPaths.ts";
import { InvoiceReportZipJobPanel, PdfMergeJobPanel } from "./JobCreationPanels.tsx";
import { JobTable } from "./JobTable.tsx";
import {
  commitJobCenterFilters,
  hasActiveJobs,
  hasPendingJobCenterFilters,
  jobStatusOptions,
  readPathLines,
} from "./jobPresentation.ts";
import { normalizeJobId } from "./jobNavigation.ts";
import { useJobCenterOperations } from "./useJobCenterOperations.ts";
import { useJobPermissions } from "./useJobPermissions.ts";

const invoiceReportType = "ExportDocument";
const jobListViewStateStorageKey = "export-doc-manager.job-list-view-state.v1";

export function JobCenterPage({ client }: { client: ExportDocManagerApiClient }) {
  const { jobPermission, reportPermission, canExportInvoiceZip, canRetryJob } = useJobPermissions();
  const workspaceDeviceProfile = useWorkspaceDeviceProfile();
  const workspaceDeviceMode = workspaceDeviceProfile.mode;
  const workspaceDeviceCapabilities = workspaceDeviceProfile.capabilities;
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const focusedJobId = normalizeJobId(searchParams.get("jobId"));
  const [initialListViewState] = useState(() => loadListViewState(jobListViewStateStorageKey));
  const [keyword, setKeyword] = useState(focusedJobId || initialListViewState.keyword);
  const [committedKeyword, setCommittedKeyword] = useState(focusedJobId || initialListViewState.keyword);
  const [status, setStatus] = useState("");
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(initialListViewState.pageSize);
  const desktopAvailable = isDesktopBridgeAvailable();
  const canCreateInvoiceReportZip =
    workspaceDeviceCapabilities.canImportExport
    && jobPermission.canOperate
    && reportPermission.canView
    && canExportInvoiceZip;

  const jobsQuery = useQuery({
    queryKey: queryKeys.jobs(pageNumber, pageSize, committedKeyword.trim(), status),
    queryFn: ({ signal }) => client.listJobs({
      status: status || undefined,
      keyword: committedKeyword.trim() || undefined,
      pageNumber,
      pageSize,
    }, { signal }),
    placeholderData: keepPreviousData,
    refetchInterval: (query) => hasActiveJobs(query.state.data?.items) ? 2_000 : false,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: true,
  });

  const reportTemplatesQuery = useQuery({
    queryKey: queryKeys.reportTemplates(invoiceReportType),
    queryFn: ({ signal }) => client.listReportTemplates({ reportType: invoiceReportType }, { signal }),
    enabled: canCreateInvoiceReportZip,
    staleTime: 5 * 60 * 1000,
  });

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: ({ signal }) => client.getSettings({ signal }),
    enabled: jobPermission.canOperate && workspaceDeviceCapabilities.canImportExport,
    staleTime: 5 * 60 * 1000,
  });

  const defaultExportDirectory = readDefaultExportDirectory(settingsQuery.data?.settings);
  const configuredReportTemplatePath = readDefaultReportTemplatePath(settingsQuery.data?.settings, "ExportDocument");
  const operations = useJobCenterOperations({
    client,
    queryClient,
    canOperate: jobPermission.canOperate,
    canManage: jobPermission.canManage,
    canCreateInvoiceReportZip,
    desktopAvailable,
    defaultExportDirectory,
    jobsCount: jobsQuery.data?.items?.length ?? 0,
    focusJob,
    clearFocusedJob,
  });
  const {
    pdfSources,
    pdfDestination,
    pdfUploadFiles,
    reportInvoiceIds,
    reportZipDestination,
    reportTemplatePath,
    reportWithSeal,
    reportInvoiceIdList,
    canStartPdfMerge,
    canStartReportZip,
  } = operations;

  useEffect(() => {
    if (jobsQuery.data && jobsQuery.data.pageNumber !== pageNumber) {
      setPageNumber(jobsQuery.data.pageNumber);
    }
  }, [jobsQuery.data, pageNumber]);

  useEffect(() => {
    if (!focusedJobId) return;
    setKeyword(focusedJobId);
    setCommittedKeyword(focusedJobId);
    setStatus("");
    setPageNumber(1);
  }, [focusedJobId]);

  useEffect(() => {
    if (focusedJobId) return;
    saveListViewState(jobListViewStateStorageKey, { keyword: committedKeyword, pageSize });
  }, [committedKeyword, focusedJobId, pageSize]);

  useEffect(() => {
    const templates = reportTemplatesQuery.data ?? [];
    if (!templates.length || settingsQuery.isFetching) return;
    const nextPath = resolveReportTemplatePath({ templates, currentPath: reportTemplatePath,
      configuredPath: configuredReportTemplatePath, fallbackFileName: "invoice_template.html" });
    if (nextPath === reportTemplatePath) return;
    const preferredTemplate = templates.find((template) => template.templatePath === nextPath);
    operations.setReportTemplatePath(nextPath);
    operations.setReportWithSeal(preferredTemplate?.withSealDefault ?? true);
  }, [configuredReportTemplatePath, operations, reportTemplatePath, reportTemplatesQuery.data, settingsQuery.isFetching]);
  function applyFilters(nextKeyword = keyword, nextStatus = status) {
    const next = commitJobCenterFilters(nextKeyword, nextStatus);
    clearFocusedJob();
    setKeyword(next.keyword); setCommittedKeyword(next.committedKeyword);
    setStatus(next.status); setPageNumber(next.pageNumber); operations.clearFeedback();
  }
  function handleSearch(event: FormEvent<HTMLFormElement>) { event.preventDefault(); applyFilters(); }
  function handleKeywordChange(value: string) { setKeyword(value); if (!value.trim()) applyFilters(value); }
  function changeStatus(value: string) { applyFilters(keyword, value); }
  function handleRefresh() {
    const normalizedKeyword = keyword.trim();
    const hasPendingFilters = hasPendingJobCenterFilters(keyword, committedKeyword, pageNumber);
    applyFilters(normalizedKeyword);
    if (!hasPendingFilters) void jobsQuery.refetch();
  }

  function handlePageSizeChange(nextPageSize: number) {
    applyFilters();
    setPageSize(normalizeListPageSize(nextPageSize));
  }

  function focusJob(jobId: string, nextMessage: string) {
    const normalizedJobId = normalizeJobId(jobId);
    operations.showSuccess(nextMessage);
    if (!normalizedJobId) return;
    setKeyword(normalizedJobId);
    setCommittedKeyword(normalizedJobId);
    setStatus("");
    setPageNumber(1);
    setSearchParams({ jobId: normalizedJobId }, { replace: true });
  }

  function clearFocusedJob() {
    if (focusedJobId) setSearchParams({}, { replace: true });
  }

  const page = jobsQuery.data ?? null;
  const jobs = page?.items ?? [];
  const totalPages = Math.max(page?.totalPages ?? 1, 1);
  const errorMessage = jobsQuery.isError ? readApiError(jobsQuery.error) : null;
  const reportTemplates = reportTemplatesQuery.data ?? [];
  const reportTemplateErrorMessage = reportTemplatesQuery.isError ? readApiError(reportTemplatesQuery.error) : null;
  const isActionBusy = operations.isBusy || reportTemplatesQuery.isFetching;

  return (
    <section className="work-surface job-center-surface" aria-label="任务中心">
      {!jobPermission.canOperate ? (
        <PermissionNotice>当前权限模板仅允许查看任务；新建、取消和重试已禁用，删除与批量清理需要管理权限。</PermissionNotice>
      ) : null}
      <WorkspaceDeviceNotice
        mode={workspaceDeviceMode}
        phone="可查看任务进度、处理失败任务和接收提醒；批量报表、PDF 合并、清理及文件导入导出请使用桌面端。"
        tablet={workspaceDeviceCapabilities.canImportExport
          ? "可创建、下载和处理文件任务；受限屏幕下不提供批量清理，密集任务管理建议使用更宽屏幕。"
          : "可查看任务进度并处理单个失败任务；连接鼠标或触控板后可创建、下载和处理文件任务。"}
      />
      {jobPermission.canOperate && workspaceDeviceCapabilities.canImportExport ? (
        <section className="job-create-panel" aria-label="新建任务">
          {canCreateInvoiceReportZip ? (
            <details>
              <summary><span>批量报表 ZIP</span><small>{reportInvoiceIdList.length} 张发票</small></summary>
              <InvoiceReportZipJobPanel
                invoiceIds={reportInvoiceIds}
                invoiceCount={reportInvoiceIdList.length}
                destinationPath={reportZipDestination}
                templatePath={reportTemplatePath}
                withSeal={reportWithSeal}
                templates={reportTemplates}
                templateErrorMessage={reportTemplateErrorMessage}
                isTemplateLoading={reportTemplatesQuery.isFetching}
                disabled={isActionBusy}
                canSubmit={canStartReportZip && !reportTemplatesQuery.isFetching}
                onInvoiceIdsChange={operations.setReportInvoiceIds}
                onDestinationPathChange={operations.setReportZipDestination}
                onTemplatePathChange={operations.setReportTemplatePath}
                onWithSealChange={operations.setReportWithSeal}
                onSubmit={() => operations.reportZipMutation.mutate()}
                onMessage={operations.handleChildMessage}
                defaultExportDirectory={defaultExportDirectory}
              />
            </details>
          ) : <PermissionNotice>当前权限可使用普通后台任务，但未同时授予发票单据输出权限，批量报表 ZIP 已隐藏。</PermissionNotice>}
          {reportPermission.canOperate ? <details>
            <summary><span>PDF 合并</span><small>{readPathLines(pdfSources).length} 个源文件</small></summary>
            {desktopAvailable ? (
              <PdfMergeJobPanel
                sourcePaths={pdfSources}
                destinationPath={pdfDestination}
                disabled={isActionBusy}
                canSubmit={canStartPdfMerge}
                onSourcePathsChange={operations.setPdfSources}
                onDestinationPathChange={operations.setPdfDestination}
                onSubmit={() => operations.pdfMergeMutation.mutate()}
                onMessage={operations.handleChildMessage}
                defaultExportDirectory={defaultExportDirectory}
              />
            ) : (
              <form className="job-tool-panel" onSubmit={(event) => { event.preventDefault(); operations.clearFeedback(); operations.pdfMergeMutation.mutate(); }}>
                <label className="inline-filter"><span>源 PDF</span><input type="file" accept="application/pdf,.pdf" multiple disabled={isActionBusy} onChange={(event) => { const files = Array.from(event.currentTarget.files ?? []); event.currentTarget.value = ""; operations.setPdfUploadFiles(files); }} /></label>
                <div className="job-tool-submit-row"><span>{pdfUploadFiles.length} 个源文件</span><button className="solid action-button" type="submit" disabled={!canStartPdfMerge}><Play size={16} aria-hidden="true" /><span>合并并下载</span></button></div>
                <div className="field-help">文件仅暂存在程序临时区，任务结束后自动清理。</div>
              </form>
            )}
          </details> : null}
        </section>
      ) : null}

      <div className="toolbar">
        <form className="search-form" onSubmit={handleSearch}>
          <Search size={17} aria-hidden="true" />
          <input aria-label="搜索任务" value={keyword} onChange={(event) => handleKeywordChange(event.target.value)} placeholder="任务号、标题、输出文件、错误" />
        </form>
        <div className="filter-bar"><FilterSelect label="状态" value={status} options={jobStatusOptions} onChange={changeStatus} /></div>
        <div className="toolbar-actions">
          <button className="icon-button" type="button" title="清除搜索" aria-label="清除搜索" disabled={jobsQuery.isFetching || operations.isBusy || (!keyword && !committedKeyword && !focusedJobId)} onClick={() => applyFilters("")}>
            <X size={18} aria-hidden="true" />
          </button>
          {workspaceDeviceCapabilities.canUseBatchOperations ? (
            <button className="command-button secondary" type="button" title="清理已完成、失败、已取消的任务记录" disabled={!jobPermission.canManage || isActionBusy || jobs.length === 0} onClick={() => void operations.handleClearFinishedJobs()}>
              <Trash2 size={17} aria-hidden="true" /><span>清理已结束</span>
            </button>
          ) : null}
          <button className="icon-button" type="button" title="刷新" aria-label="刷新" disabled={jobsQuery.isFetching || operations.isBusy} onClick={handleRefresh}>
            <RefreshCw size={18} aria-hidden="true" />
          </button>
        </div>
      </div>

      {errorMessage ? <InlineNotice tone="error" title="任务中心操作失败">{errorMessage}</InlineNotice> : null}
      {operations.message ? <InlineNotice tone={operations.messageTone}>{operations.message}</InlineNotice> : null}

      <JobTable
        data={jobs}
        focusedJobId={focusedJobId}
        isBusy={jobsQuery.isPending || operations.isBusy}
        hasError={Boolean(errorMessage)}
        canOperate={jobPermission.canOperate}
        canRetry={canRetryJob}
        canManage={jobPermission.canManage}
        canDownload={workspaceDeviceCapabilities.canImportExport}
        onMessage={operations.handleChildMessage}
        onCancel={(job: BackgroundJobSnapshot) => void operations.handleCancelJob(job)}
        onRetry={(jobId) => operations.retryMutation.mutate(jobId)}
        onDelete={(job: BackgroundJobSnapshot) => void operations.handleDeleteJob(job)}
        onDownload={(job: BackgroundJobSnapshot) => operations.downloadMutation.mutate(job)}
        desktopAvailable={desktopAvailable}
      />

      <ListPaginationControls
        pageNumber={page?.pageNumber ?? pageNumber}
        totalPages={totalPages}
        totalCount={page?.totalCount ?? 0}
        pageSize={pageSize}
        pageSizeOptions={listPageSizeOptions}
        isBusy={operations.isBusy}
        onPageChange={setPageNumber}
        onPageSizeChange={handlePageSizeChange}
      />
    </section>
  );
}

function FilterSelect({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: string;
  options: Array<{ value: string; label: string }>;
  onChange: (value: string) => void;
}) {
  return (
    <label className="inline-filter">
      <span>{label}</span>
      <select value={value} onChange={(event) => onChange(event.target.value)}>
        <option value="">全部</option>
        {options.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
      </select>
    </label>
  );
}
