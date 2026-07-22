import { FormEvent, KeyboardEvent, useEffect, useState } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Ban, Download, FileArchive, FileStack, Play, RefreshCw, Save, Search, Trash2 } from "lucide-react";
import { useSearchParams } from "react-router-dom";
import { type ApiReportTemplateDto, type BackgroundJobSnapshot, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import {
  isDesktopBridgeAvailable,
  selectPdfFiles,
  selectSavePdfPath,
  selectSaveZipPath,
} from "../../desktop/desktopBridge.ts";
import { DesktopIconButton, readDesktopError, renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { SelectField } from "../../ui/FormFields.tsx";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { listPageSizeOptions, loadListViewState, normalizeListPageSize, saveListViewState } from "../../ui/listViewState.ts";
import { PathField, PathTextAreaField } from "../../ui/PathField.tsx";
import { formatPlainNumber, readApiError } from "../../ui/formUtils.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { downloadJobResultWhenReady } from "../../ui/downloadJobResult.ts";
import { readDefaultExportDirectory } from "../settings/settingsPaths.ts";
import { normalizeJobId } from "./jobNavigation.ts";

const invoiceReportType = "ExportDocument";
const jobListViewStateStorageKey = "export-doc-manager.job-list-view-state.v1";

const jobStatusOptions = [
  { value: "Queued", label: "排队中" },
  { value: "Running", label: "运行中" },
  { value: "Succeeded", label: "已完成" },
  { value: "Failed", label: "失败" },
  { value: "Canceling", label: "取消中" },
  { value: "Canceled", label: "已取消" },
];

export function JobCenterPage({ client }: { client: ExportDocManagerApiClient }) {
  const jobPermission = useModulePermission("document.jobs");
  const reportPermission = useModulePermission("document.reports");
  const invoiceReportPermission = useModulePermission("document.invoice-reports");
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const focusedJobId = normalizeJobId(searchParams.get("jobId"));
  const [initialListViewState] = useState(() => loadListViewState(jobListViewStateStorageKey));
  const [keyword, setKeyword] = useState(focusedJobId || initialListViewState.keyword);
  const [committedKeyword, setCommittedKeyword] = useState(focusedJobId || initialListViewState.keyword);
  const [status, setStatus] = useState("");
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(initialListViewState.pageSize);
  const [message, setMessage] = useState<string | null>(null);
  const [pdfSources, setPdfSources] = useState("");
  const [pdfDestination, setPdfDestination] = useState("");
  const [pdfUploadFiles, setPdfUploadFiles] = useState<File[]>([]);
  const [reportInvoiceIds, setReportInvoiceIds] = useState("");
  const [reportZipDestination, setReportZipDestination] = useState("");
  const [reportTemplatePath, setReportTemplatePath] = useState("");
  const [reportWithSeal, setReportWithSeal] = useState(true);
  const desktopAvailable = isDesktopBridgeAvailable();
  const canCreateInvoiceReportZip =
    jobPermission.canOperate && reportPermission.canView && invoiceReportPermission.canOperate;

  const jobsQuery = useQuery({
    queryKey: queryKeys.jobs(pageNumber, pageSize, committedKeyword.trim(), status),
    queryFn: () =>
      client.listJobs({
        status: status || undefined,
        keyword: committedKeyword.trim() || undefined,
        pageNumber,
        pageSize,
      }),
    placeholderData: keepPreviousData,
  });

  const reportTemplatesQuery = useQuery({
    queryKey: queryKeys.reportTemplates(invoiceReportType),
    queryFn: () => client.listReportTemplates({ reportType: invoiceReportType }),
    enabled: canCreateInvoiceReportZip,
    staleTime: 5 * 60 * 1000,
  });

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: () => client.getSettings(),
    enabled: jobPermission.canOperate,
    staleTime: 5 * 60 * 1000,
  });

  useEffect(() => {
    if (jobsQuery.data && jobsQuery.data.pageNumber !== pageNumber) {
      setPageNumber(jobsQuery.data.pageNumber);
    }
  }, [jobsQuery.data, pageNumber]);

  useEffect(() => {
    if (!focusedJobId) {
      return;
    }

    setKeyword(focusedJobId);
    setCommittedKeyword(focusedJobId);
    setStatus("");
    setPageNumber(1);
  }, [focusedJobId]);

  useEffect(() => {
    if (focusedJobId) {
      return;
    }

    saveListViewState(jobListViewStateStorageKey, {
      keyword: committedKeyword,
      pageSize,
    });
  }, [committedKeyword, focusedJobId, pageSize]);

  useEffect(() => {
    if (!reportTemplatesQuery.data?.length || reportTemplatePath) {
      return;
    }

    const preferredTemplate = findPreferredInvoiceTemplate(reportTemplatesQuery.data);
    setReportTemplatePath(preferredTemplate.templatePath);
    setReportWithSeal(preferredTemplate.withSealDefault);
  }, [reportTemplatePath, reportTemplatesQuery.data]);

  const cancelMutation = useMutation({
    mutationFn: (jobId: string) => client.cancelJob({ jobId }),
    onSuccess: async (response) => {
      setMessage(response.message || "已请求取消任务。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
    },
  });

  const retryMutation = useMutation({
    mutationFn: (jobId: string) => client.retryJob({ jobId }),
    onSuccess: async (job) => {
      focusJob(job.jobId, `已重新创建任务：${job.jobId}`);
      await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (jobId: string) => client.deleteJob({ jobId }),
    onSuccess: async (response) => {
      setMessage(response.message || "已删除任务记录。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
    },
  });

  const clearFinishedMutation = useMutation({
    mutationFn: () => client.clearFinishedJobs(),
    onSuccess: async (response) => {
      setMessage(response.message || "已清理已结束任务记录。");
      clearFocusedJob();
      await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
    },
  });

  const pdfMergeMutation = useMutation({
    mutationFn: async () => {
      if (!desktopAvailable) {
        const form = new FormData();
        pdfUploadFiles.forEach((file) => form.append("files", file, file.name));
        const job = await client.uploadAndStartPdfMergeDownloadJob({ body: form });
        await downloadJobResultWhenReady(client, job, "merged.pdf");
        return job;
      }

      return client.startPdfMergeSaveToPathJob({
        body: {
          sourceFiles: readPathLines(pdfSources),
          destinationPath: pdfDestination.trim(),
        },
      });
    },
    onSuccess: async (job) => {
      focusJob(job.jobId, `已创建 PDF 合并任务：${job.jobId}`);
      setPdfDestination("");
      setPdfUploadFiles([]);
      await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
    },
  });

  const reportZipMutation = useMutation({
    mutationFn: async () => {
      const body = {
        invoiceIds: readPositiveIntegerTokens(reportInvoiceIds),
        reportType: invoiceReportType,
        templatePath: reportTemplatePath.trim(),
        withSeal: reportWithSeal,
        destinationPath: desktopAvailable ? reportZipDestination.trim() : "",
      };
      const job = desktopAvailable
        ? await client.startInvoiceReportPdfZipSaveToPathJob({ body })
        : await client.startInvoiceReportPdfZipDownloadJob({ body });
      if (!desktopAvailable) {
        await downloadJobResultWhenReady(client, job, "invoice-reports.zip");
      }
      return job;
    },
    onSuccess: async (job) => {
      focusJob(job.jobId, `已创建批量报表 ZIP 任务：${job.jobId}`);
      setReportZipDestination("");
      await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
    },
  });

  const downloadMutation = useMutation({
    mutationFn: async (job: BackgroundJobSnapshot) => {
      const blob = await client.downloadJobResult({ jobId: job.jobId });
      const fileName = fileNameFromPath(job.outputPath) || `${job.kind || "download"}.bin`;
      downloadBlob(blob, fileName);
    },
    onError: (error) => setMessage(readApiError(error)),
  });

  function handleSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    clearFocusedJob();
    setCommittedKeyword(keyword.trim());
    setPageNumber(1);
    setMessage(null);
  }

  function changeStatus(value: string) {
    clearFocusedJob();
    setStatus(value);
    setPageNumber(1);
    setMessage(null);
  }

  function handlePageSizeChange(nextPageSize: number) {
    setPageSize(normalizeListPageSize(nextPageSize));
    setPageNumber(1);
    setMessage(null);
  }

  function focusJob(jobId: string, nextMessage: string) {
    const normalizedJobId = normalizeJobId(jobId);
    setMessage(nextMessage);
    if (!normalizedJobId) {
      return;
    }

    setKeyword(normalizedJobId);
    setCommittedKeyword(normalizedJobId);
    setStatus("");
    setPageNumber(1);
    setSearchParams({ jobId: normalizedJobId }, { replace: true });
  }

  function clearFocusedJob() {
    if (focusedJobId) {
      setSearchParams({}, { replace: true });
    }
  }

  const page = jobsQuery.data ?? null;
  const jobs = page?.items ?? [];
  const totalPages = Math.max(page?.totalPages ?? 1, 1);
  const errorMessage = jobsQuery.isError ? readApiError(jobsQuery.error) : null;
  const isBusy =
    jobsQuery.isFetching ||
    cancelMutation.isPending ||
    retryMutation.isPending ||
    deleteMutation.isPending ||
    clearFinishedMutation.isPending ||
    pdfMergeMutation.isPending ||
    reportZipMutation.isPending;
  const pdfSourceFiles = readPathLines(pdfSources);
  const canStartPdfMerge = desktopAvailable
    ? pdfSourceFiles.length > 0 && Boolean(pdfDestination.trim()) && !isBusy
    : pdfUploadFiles.length >= 2 && !isBusy;
  const reportInvoiceIdList = readPositiveIntegerTokens(reportInvoiceIds);
  const reportTemplates = reportTemplatesQuery.data ?? [];
  const reportTemplateErrorMessage = reportTemplatesQuery.isError ? readApiError(reportTemplatesQuery.error) : null;
  const defaultExportDirectory = readDefaultExportDirectory(settingsQuery.data?.settings);
  const canStartReportZip =
    reportInvoiceIdList.length > 0 &&
    (!desktopAvailable || Boolean(reportZipDestination.trim())) &&
    !isBusy &&
    !reportTemplatesQuery.isFetching;

  return (
    <section className="work-surface job-center-surface" aria-label="任务中心">
      {!jobPermission.canOperate ? (
        <PermissionNotice>当前权限模板仅允许查看任务；新建、取消和重试已禁用，删除与批量清理需要管理权限。</PermissionNotice>
      ) : null}
      {jobPermission.canOperate ? <section className="job-create-panel" aria-label="新建任务">
        {canCreateInvoiceReportZip ? <details>
          <summary>
            <span>批量报表 ZIP</span>
            <small>{reportInvoiceIdList.length} 张发票</small>
          </summary>
          <InvoiceReportZipJobPanel
            invoiceIds={reportInvoiceIds}
            invoiceCount={reportInvoiceIdList.length}
            destinationPath={reportZipDestination}
            templatePath={reportTemplatePath}
            withSeal={reportWithSeal}
            templates={reportTemplates}
            templateErrorMessage={reportTemplateErrorMessage}
            isTemplateLoading={reportTemplatesQuery.isFetching}
            disabled={isBusy}
            canSubmit={canStartReportZip}
            onInvoiceIdsChange={setReportInvoiceIds}
            onDestinationPathChange={setReportZipDestination}
            onTemplatePathChange={setReportTemplatePath}
            onWithSealChange={setReportWithSeal}
            onSubmit={() => reportZipMutation.mutate()}
            onMessage={setMessage}
            defaultExportDirectory={defaultExportDirectory}
          />
        </details> : (
          <PermissionNotice>当前权限可使用普通后台任务，但未同时授予发票单据输出权限，批量报表 ZIP 已隐藏。</PermissionNotice>
        )}
        <details>
          <summary>
            <span>PDF 合并</span>
            <small>{pdfSourceFiles.length} 个源文件</small>
          </summary>
          {desktopAvailable ? <PdfMergeJobPanel
            sourcePaths={pdfSources}
            destinationPath={pdfDestination}
            disabled={isBusy}
            canSubmit={canStartPdfMerge}
            onSourcePathsChange={setPdfSources}
            onDestinationPathChange={setPdfDestination}
            onSubmit={() => pdfMergeMutation.mutate()}
            onMessage={setMessage}
            defaultExportDirectory={defaultExportDirectory}
          /> : <form className="job-tool-panel" onSubmit={(event) => { event.preventDefault(); setMessage(null); pdfMergeMutation.mutate(); }}>
            <label className="inline-filter"><span>源 PDF</span><input type="file" accept="application/pdf,.pdf" multiple disabled={isBusy} onChange={(event) => setPdfUploadFiles(Array.from(event.target.files ?? []))} /></label>
            <div className="job-tool-submit-row"><span>{pdfUploadFiles.length} 个源文件</span><button className="solid action-button" type="submit" disabled={!canStartPdfMerge}><Play size={16} aria-hidden="true" /><span>合并并下载</span></button></div>
            <div className="field-help">文件仅暂存在运行数据根，任务结束后自动清理。</div>
          </form>}
        </details>
      </section> : null}

      <div className="toolbar">
        <form className="search-form" onSubmit={handleSearch}>
          <Search size={17} aria-hidden="true" />
          <input
            aria-label="搜索任务"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            placeholder="任务号、标题、输出路径、错误"
          />
        </form>
        <div className="filter-bar">
          <FilterSelect label="状态" value={status} options={jobStatusOptions} onChange={changeStatus} />
        </div>
        <div className="toolbar-actions">
          <button
            className="command-button secondary"
            type="button"
            title="清理已完成、失败、已取消的任务记录"
            disabled={!jobPermission.canManage || isBusy || jobs.length === 0}
            onClick={() => {
              if (jobPermission.canManage) clearFinishedMutation.mutate();
            }}
          >
            <Trash2 size={17} aria-hidden="true" />
            <span>清理已结束</span>
          </button>
          <button
            className="icon-button"
            type="button"
            title="刷新" aria-label="刷新"
            disabled={isBusy}
            onClick={() => void jobsQuery.refetch()}
          >
            <RefreshCw size={18} aria-hidden="true" />
          </button>
        </div>
      </div>

      {errorMessage ? <InlineNotice tone="error" title="任务中心操作失败">{errorMessage}</InlineNotice> : null}
      {message ? (
        <InlineNotice tone={cancelMutation.isError || retryMutation.isError || deleteMutation.isError || clearFinishedMutation.isError || pdfMergeMutation.isError || reportZipMutation.isError ? "error" : "success"}>
          {message}
        </InlineNotice>
      ) : null}

      <JobTable
        data={jobs}
        focusedJobId={focusedJobId}
        isBusy={isBusy}
        canOperate={jobPermission.canOperate}
        canManage={jobPermission.canManage}
        onMessage={setMessage}
        onCancel={(jobId) => cancelMutation.mutate(jobId)}
        onRetry={(jobId) => retryMutation.mutate(jobId)}
        onDelete={(jobId) => deleteMutation.mutate(jobId)}
        onDownload={(job) => downloadMutation.mutate(job)}
        desktopAvailable={desktopAvailable}
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

function InvoiceReportZipJobPanel({
  invoiceIds,
  invoiceCount,
  destinationPath,
  templatePath,
  withSeal,
  templates,
  templateErrorMessage,
  isTemplateLoading,
  disabled,
  canSubmit,
  onInvoiceIdsChange,
  onDestinationPathChange,
  onTemplatePathChange,
  onWithSealChange,
  onSubmit,
  onMessage,
  defaultExportDirectory,
}: {
  invoiceIds: string;
  invoiceCount: number;
  destinationPath: string;
  templatePath: string;
  withSeal: boolean;
  templates: ApiReportTemplateDto[];
  templateErrorMessage: string | null;
  isTemplateLoading: boolean;
  disabled: boolean;
  canSubmit: boolean;
  onInvoiceIdsChange: (value: string) => void;
  onDestinationPathChange: (value: string) => void;
  onTemplatePathChange: (value: string) => void;
  onWithSealChange: (value: boolean) => void;
  onSubmit: () => void;
  onMessage: (message: string | null) => void;
  defaultExportDirectory: string;
}) {
  const desktopAvailable = isDesktopBridgeAvailable();

  function handleTemplateChange(value: string) {
    onTemplatePathChange(value);
    const template = templates.find((item) => item.templatePath === value);
    if (template) {
      onWithSealChange(template.withSealDefault);
    }
    onMessage(null);
  }

  async function pickDestination() {
    try {
      const selected = await selectSaveZipPath("invoice-reports.zip", defaultExportDirectory);
      if (selected) {
        onDestinationPathChange(selected);
        onMessage(null);
      }
    } catch (error) {
      onMessage(readDesktopError(error));
    }
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    onMessage(null);
    onSubmit();
  }

  return (
    <form className="job-tool-panel" aria-label="批量报表 ZIP 任务" onSubmit={handleSubmit}>
      {templateErrorMessage ? <InlineNotice tone="warning" title="报表模板未完整加载">{templateErrorMessage}</InlineNotice> : null}

      <div className="job-tool-grid job-report-zip-grid">
        <PathTextAreaField
          label="发票 ID"
          value={invoiceIds}
          disabled={disabled}
          onChange={(value) => {
            onInvoiceIdsChange(value);
            onMessage(null);
          }}
        />
        <div className="job-tool-stack">
          <div className="report-zip-options">
            <SelectField
              label="模板"
              value={templatePath}
              disabled={disabled || isTemplateLoading || templates.length === 0}
              options={templates.map((template) => ({
                value: template.templatePath,
                label: template.displayName || fileNameFromPath(template.templatePath),
              }))}
              onChange={handleTemplateChange}
            />
            <label className="toggle-field">
              <input
                type="checkbox"
                checked={withSeal}
                disabled={disabled}
                onChange={(event) => {
                  onWithSealChange(event.target.checked);
                  onMessage(null);
                }}
              />
              <span>带章</span>
            </label>
          </div>
          {desktopAvailable ? <PathField
            label="输出 ZIP"
            value={destinationPath}
            disabled={disabled}
            onChange={(value) => {
              onDestinationPathChange(value);
              onMessage(null);
            }}
            actions={
              <>
                {desktopAvailable ? (
                  <DesktopIconButton title="选择保存位置" disabled={disabled} onClick={pickDestination}>
                    <FileArchive size={15} aria-hidden="true" />
                  </DesktopIconButton>
                ) : null}
                {renderOpenPathAction(destinationPath, "打开输出位置", onMessage)}
              </>
            }
          /> : <div className="field-help">ZIP 将保存到浏览器默认下载目录。</div>}
        </div>
      </div>
      <div className="job-tool-submit-row">
        <span>{invoiceCount} 张发票</span>
        <button className="solid action-button" type="submit" disabled={!canSubmit}>
          <Play size={16} aria-hidden="true" />
          <span>{desktopAvailable ? "开始" : "生成并下载"}</span>
        </button>
      </div>
    </form>
  );
}

function PdfMergeJobPanel({
  sourcePaths,
  destinationPath,
  disabled,
  canSubmit,
  onSourcePathsChange,
  onDestinationPathChange,
  onSubmit,
  onMessage,
  defaultExportDirectory,
}: {
  sourcePaths: string;
  destinationPath: string;
  disabled: boolean;
  canSubmit: boolean;
  onSourcePathsChange: (value: string) => void;
  onDestinationPathChange: (value: string) => void;
  onSubmit: () => void;
  onMessage: (message: string | null) => void;
  defaultExportDirectory: string;
}) {
  const desktopAvailable = isDesktopBridgeAvailable();

  async function pickPdfSources() {
    try {
      const selected = await selectPdfFiles();
      if (selected.length > 0) {
        const merged = [...readPathLines(sourcePaths), ...selected]
          .filter((value, index, values) => values.findIndex((item) => item.toLowerCase() === value.toLowerCase()) === index)
          .join("\n");
        onSourcePathsChange(merged);
      }
    } catch (error) {
      onMessage(readDesktopError(error));
    }
  }

  async function pickDestination() {
    try {
      const selected = await selectSavePdfPath("merged.pdf", defaultExportDirectory);
      if (selected) {
        onDestinationPathChange(selected);
      }
    } catch (error) {
      onMessage(readDesktopError(error));
    }
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    onMessage(null);
    onSubmit();
  }

  return (
    <form className="job-tool-panel" aria-label="PDF 合并任务" onSubmit={handleSubmit}>
      <div className="job-tool-grid">
        <PathTextAreaField
          label="源 PDF"
          value={sourcePaths}
          disabled={disabled}
          onChange={onSourcePathsChange}
          actions={
            desktopAvailable ? (
              <DesktopIconButton title="选择 PDF 文件" disabled={disabled} onClick={pickPdfSources}>
                <FileStack size={15} aria-hidden="true" />
              </DesktopIconButton>
            ) : undefined
          }
        />
        <PathField
          label="输出 PDF"
          value={destinationPath}
          disabled={disabled}
          onChange={onDestinationPathChange}
          actions={
            <>
              {desktopAvailable ? (
                <DesktopIconButton title="选择保存位置" disabled={disabled} onClick={pickDestination}>
                  <Save size={15} aria-hidden="true" />
                </DesktopIconButton>
              ) : null}
              {renderOpenPathAction(destinationPath, "打开输出位置", onMessage)}
            </>
          }
        />
      </div>
      <div className="job-tool-submit-row">
        <span>{readPathLines(sourcePaths).length} 个源文件</span>
        <button className="solid action-button" type="submit" disabled={!canSubmit}>
          <Play size={16} aria-hidden="true" />
          <span>开始</span>
        </button>
      </div>
    </form>
  );
}

function JobTable({
  data,
  focusedJobId,
  isBusy,
  canOperate,
  canManage,
  onMessage,
  onCancel,
  onRetry,
  onDelete,
  onDownload,
  desktopAvailable,
}: {
  data: BackgroundJobSnapshot[];
  focusedJobId: string;
  isBusy: boolean;
  canOperate: boolean;
  canManage: boolean;
  onMessage: (message: string | null) => void;
  onCancel: (jobId: string) => void;
  onRetry: (jobId: string) => void;
  onDelete: (jobId: string) => void;
  onDownload: (job: BackgroundJobSnapshot) => void;
  desktopAvailable: boolean;
}) {
  function handleRowKeyDown(event: KeyboardEvent<HTMLTableRowElement>) {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
    }
  }

  return (
    <ResponsiveTableFrame label="后台任务列表" busy={isBusy} mobileLayout="scroll">
      <table className="job-table">
        <thead>
          <tr>
            <th>任务</th>
            <th>类型</th>
            <th>状态</th>
            <th className="amount-cell">进度</th>
            <th>消息</th>
            <th>输出</th>
            <th>创建</th>
            <th>完成</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          {data.length === 0 ? (
            <tr>
              <td colSpan={9} className="empty-cell">
                {isBusy ? "加载中" : "暂无任务"}
              </td>
            </tr>
          ) : (
            data.map((job) => {
              const isFocused = Boolean(focusedJobId) && job.jobId.toLowerCase() === focusedJobId.toLowerCase();
              const canDelete = isTerminalJob(job.status);
              return (
              <tr
                key={job.jobId}
                className={isFocused ? "job-row-focused" : undefined}
                tabIndex={0}
                onKeyDown={handleRowKeyDown}
              >
                <td>
                  <div className="job-title-cell">
                    <strong title={job.title}>{job.title || job.jobId}</strong>
                    <span title={job.jobId}>{job.jobId}</span>
                  </div>
                </td>
                <td>{job.kind || "-"}</td>
                <td>
                  <span className="status-pill">{formatJobStatus(job.status)}</span>
                </td>
                <td className="amount-cell">{formatProgress(job.progressPercent)}</td>
                <td className="message-cell" title={readJobMessage(job)}>
                  {readJobMessage(job)}
                </td>
                <td className="path-cell" title={desktopAvailable ? job.outputPath : undefined}>
                  <div className="table-path-cell job-output-path-cell">
                    <span>{desktopAvailable ? (job.outputPath || "-") : (job.outputPath ? fileNameFromPath(job.outputPath) : "-")}</span>
                    {desktopAvailable && job.outputPath?.trim() ? renderOpenPathAction(job.outputPath, "打开任务输出", onMessage) : null}
                    {!desktopAvailable && job.status.toLowerCase() === "succeeded" && job.outputPath ? (
                      <button className="icon-button compact-icon-button" type="button" title="下载任务结果" aria-label="下载任务结果" onClick={() => onDownload(job)}>
                        <Download size={16} aria-hidden="true" />
                      </button>
                    ) : null}
                  </div>
                </td>
                <td>{formatDateTime(job.createdAt)}</td>
                <td>{formatDateTime(job.completedAt)}</td>
                <td>
                  <div className="job-row-actions">
                    <button
                      className="icon-button compact-icon-button"
                      type="button"
                      title="重试任务" aria-label="重试任务"
                      disabled={!canOperate || isBusy || !job.canRetry}
                      onClick={() => onRetry(job.jobId)}
                    >
                      <RefreshCw size={16} aria-hidden="true" />
                    </button>
                    <button
                      className="icon-button compact-icon-button"
                      type="button"
                      title="取消任务" aria-label="取消任务"
                      disabled={!canOperate || isBusy || !job.canCancel}
                      onClick={() => onCancel(job.jobId)}
                    >
                      <Ban size={16} aria-hidden="true" />
                    </button>
                    <button
                      className="icon-button compact-icon-button"
                      type="button"
                      title="删除任务记录" aria-label="删除任务记录"
                      disabled={!canManage || isBusy || !canDelete}
                      onClick={() => onDelete(job.jobId)}
                    >
                      <Trash2 size={16} aria-hidden="true" />
                    </button>
                  </div>
                </td>
              </tr>
              );
            })
          )}
        </tbody>
      </table>
    </ResponsiveTableFrame>
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
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </label>
  );
}

function formatJobStatus(value?: string) {
  return jobStatusOptions.find((option) => option.value.toLowerCase() === value?.toLowerCase())?.label ?? value ?? "-";
}

function isTerminalJob(value?: string) {
  const normalized = value?.toLowerCase();
  return normalized === "succeeded" || normalized === "failed" || normalized === "canceled";
}

function formatProgress(value?: number) {
  return typeof value === "number" ? `${formatPlainNumber(value)}%` : "-";
}

function readJobMessage(job: BackgroundJobSnapshot) {
  return job.errorMessage || job.detailText || job.statusText || "-";
}

function formatDateTime(value?: string) {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleString("zh-CN", {
        hour12: false,
      });
}

function readPathLines(value: string) {
  return value
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean);
}

function readPositiveIntegerTokens(value: string) {
  const seen = new Set<number>();
  const result: number[] = [];

  for (const token of value.split(/[\s,;，；]+/)) {
    const trimmed = token.trim();
    if (!/^\d+$/.test(trimmed)) {
      continue;
    }

    const parsed = Number.parseInt(trimmed, 10);
    if (parsed > 0 && !seen.has(parsed)) {
      seen.add(parsed);
      result.push(parsed);
    }
  }

  return result;
}

function findPreferredInvoiceTemplate(templates: ApiReportTemplateDto[]) {
  return templates.find((template) => fileNameFromPath(template.templatePath).toLowerCase() === "invoice_template.html") ?? templates[0];
}

function fileNameFromPath(value: string) {
  return value.split(/[\\/]/).filter(Boolean).at(-1) ?? value;
}
