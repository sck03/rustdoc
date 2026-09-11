import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Download, FileSpreadsheet, Play, Save } from "lucide-react";
import {
  type BackgroundJobSnapshot,
  ExportDocManagerApiClient,
} from "../../../api/index.ts";
import { queryKeys } from "../../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, selectExcelFile, selectSaveExcelPath } from "../../../desktop/desktopBridge.ts";
import { DesktopIconButton, readDesktopError, renderOpenPathAction } from "../../../ui/DesktopPathActions.tsx";
import { Link } from "react-router-dom";
import { PathField } from "../../../ui/PathField.tsx";
import { readApiError } from "../../../ui/formUtils.ts";
import { downloadJobResultWhenReady } from "../../../ui/downloadJobResult.ts";
import { ViewJobButton } from "../../jobs/ViewJobButton.tsx";
import { InlineNotice } from "../../../ui/PageState.tsx";
import { readDefaultExportDirectory } from "../../settings/settingsPaths.ts";
import { useAbortableOperation } from "../../../ui/useAbortableOperation.ts";

export function ExcelToolsPanel({
  client,
  canOperate,
  canReadInvoices,
}: {
  client: ExportDocManagerApiClient;
  canOperate: boolean;
  canReadInvoices: boolean;
}) {
  const queryClient = useQueryClient();
  const runAbortableOperation = useAbortableOperation();
  const desktopAvailable = isDesktopBridgeAvailable();
  const [message, setMessage] = useState<string | null>(null);
  const [messageType, setMessageType] = useState<"success" | "error">("success");
  const [lastCreatedJobId, setLastCreatedJobId] = useState<string | null>(null);
  const [templateDestinationPath, setTemplateDestinationPath] = useState("");
  const [blankBookingDestinationPath, setBlankBookingDestinationPath] = useState("");
  const [convertSourcePath, setConvertSourcePath] = useState("");
  const [convertDestinationPath, setConvertDestinationPath] = useState("");
  const [convertUploadFile, setConvertUploadFile] = useState<File | null>(null);

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: ({ signal }) => client.getSettings({ signal }),
    staleTime: 5 * 60 * 1000,
  });
  const defaultExportDirectory = readDefaultExportDirectory(settingsQuery.data?.settings);

  async function refreshJobs() {
    await queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() });
  }

  function showSuccess(value: string, jobId?: string) {
    setMessage(value);
    setMessageType("success");
    setLastCreatedJobId(jobId ?? null);
  }

  function showError(value: string) {
    setMessage(value);
    setMessageType("error");
    setLastCreatedJobId(null);
  }

  function handleMutationError(error: unknown) {
    showError(readApiError(error));
  }

  async function handleJobAccepted(job: BackgroundJobSnapshot, label: string) {
    showSuccess(`已创建${label}任务：${job.jobId}`, job.jobId);
    await refreshJobs();
  }

  const templateExportMutation = useMutation({
    mutationFn: async (destinationPath: string) => runAbortableOperation(async (signal) => {
      const job = desktopAvailable
        ? await client.startExcelTemplateSaveToPathJob({ body: { destinationPath } }, { signal })
        : await client.startExcelTemplateDownloadJob({ signal });
      if (!desktopAvailable) {
        await downloadJobResultWhenReady(client, job, "导入数据模板.xlsx", { signal });
      }
      return job;
    }),
    onSuccess: async (job) => {
      await handleJobAccepted(job, desktopAvailable ? "Excel 模板导出" : "Excel 模板下载");
    },
    onError: handleMutationError,
  });

  const blankBookingExportMutation = useMutation({
    mutationFn: async (destinationPath: string) => runAbortableOperation(async (signal) => {
      const job = desktopAvailable
        ? await client.startBlankBookingSheetSaveToPathJob({ body: { destinationPath } }, { signal })
        : await client.startBlankBookingSheetDownloadJob({ signal });
      if (!desktopAvailable) {
        await downloadJobResultWhenReady(client, job, "空白托单模板.xlsx", { signal });
      }
      return job;
    }),
    onSuccess: async (job) => {
      await handleJobAccepted(job, "空白托单导出");
    },
    onError: handleMutationError,
  });

  const bookingConvertMutation = useMutation({
    mutationFn: async ({ sourcePath, destinationPath, uploadFile }: { sourcePath: string; destinationPath: string; uploadFile?: File | null }) => runAbortableOperation(async (signal) => {
      const job = desktopAvailable
        ? await client.startBookingSheetConvertSaveToPathJob({ body: { sourcePath, destinationPath } }, { signal })
        : await client.uploadAndStartBookingSheetConvertDownloadJob({
            fileName: uploadFile?.name,
            body: uploadFile ?? new Blob(),
          }, { signal });
      if (!desktopAvailable) {
        const baseName = (uploadFile?.name || "BookingSheet").replace(/\.[^.]+$/, "");
        await downloadJobResultWhenReady(client, job, `${baseName}-BookingSheet.xlsx`, { signal });
      }
      return job;
    }),
    onSuccess: async (job) => {
      await handleJobAccepted(job, "托单转换");
    },
    onError: handleMutationError,
  });

  const isBusy =
    templateExportMutation.isPending ||
    blankBookingExportMutation.isPending ||
    bookingConvertMutation.isPending;
  const canExportTemplate = canOperate && !isBusy;
  const canExportTemplateByPath = canOperate && Boolean(templateDestinationPath.trim()) && !isBusy;
  const canExportBlankBooking = canOperate && !isBusy;
  const canExportBlankBookingByPath = canOperate && Boolean(blankBookingDestinationPath.trim()) && !isBusy;
  const canConvertBooking =
    canOperate && (desktopAvailable ? !isBusy : Boolean(convertUploadFile) && !isBusy);
  const canConvertBookingByPath = canOperate && Boolean(convertSourcePath.trim()) && Boolean(convertDestinationPath.trim()) && !isBusy;
  async function pickConvertSource() {
    try {
      const selected = await selectExcelFile();
      if (selected) {
        setConvertSourcePath(selected);
        setMessage(null);
      }
    } catch (error) {
      showError(readDesktopError(error));
    }
  }

  async function pickExcelDestination(defaultFileName: string, onChange: (value: string) => void) {
    try {
      const selected = await selectSaveExcelPath(defaultFileName, defaultExportDirectory);
      if (selected) {
        onChange(selected);
        setMessage(null);
      }
    } catch (error) {
      showError(readDesktopError(error));
    }
  }

  async function chooseExcelDestination(defaultFileName: string) {
    return selectSaveExcelPath(defaultFileName, defaultExportDirectory);
  }

  async function handleExportTemplate() {
    if (!canExportTemplate) {
      return;
    }

    if (desktopAvailable) {
      try {
        const destinationPath = await chooseExcelDestination("导入数据模板.xlsx");
        if (!destinationPath) {
          return;
        }

        setTemplateDestinationPath(destinationPath);
        templateExportMutation.mutate(destinationPath);
      } catch (error) {
        showError(readDesktopError(error));
      }
      return;
    }

    templateExportMutation.mutate("");
  }

  function handleExportTemplateByPath() {
    const destinationPath = templateDestinationPath.trim();
    if (destinationPath && canExportTemplateByPath) {
      templateExportMutation.mutate(destinationPath);
    }
  }

  async function handleExportBlankBooking() {
    if (!canExportBlankBooking) {
      return;
    }

    if (desktopAvailable) {
      try {
        const destinationPath = await chooseExcelDestination("空白托单模板.xlsx");
        if (!destinationPath) {
          return;
        }

        setBlankBookingDestinationPath(destinationPath);
        blankBookingExportMutation.mutate(destinationPath);
      } catch (error) {
        showError(readDesktopError(error));
      }
      return;
    }

    blankBookingExportMutation.mutate("");
  }

  function handleExportBlankBookingByPath() {
    const destinationPath = blankBookingDestinationPath.trim();
    if (destinationPath && canExportBlankBookingByPath) {
      blankBookingExportMutation.mutate(destinationPath);
    }
  }

  async function handleConvertBooking() {
    if (!canConvertBooking) {
      return;
    }

    if (desktopAvailable) {
      try {
        const sourcePath = await selectExcelFile();
        if (!sourcePath) {
          return;
        }

        const destinationPath = await chooseExcelDestination(buildBookingSheetFileName(sourcePath));
        if (!destinationPath) {
          return;
        }

        setConvertSourcePath(sourcePath);
        setConvertDestinationPath(destinationPath);
        bookingConvertMutation.mutate({ sourcePath, destinationPath });
      } catch (error) {
        showError(readDesktopError(error));
      }
      return;
    }

    if (convertUploadFile) {
      bookingConvertMutation.mutate({ sourcePath: "", destinationPath: "", uploadFile: convertUploadFile });
    }
  }

  function handleConvertBookingByPath() {
    const sourcePath = convertSourcePath.trim();
    const destinationPath = convertDestinationPath.trim();
    if (sourcePath && destinationPath && canConvertBookingByPath) {
      bookingConvertMutation.mutate({ sourcePath, destinationPath });
    }
  }

  return (
    <section className="job-tool-panel" aria-label="Excel 模板与托单">
      <div className="tool-panel-heading">
        <div>
          <h2>Excel 模板与托单</h2>
          <span>导入模板、空白托单与托单转换</span>
        </div>
      </div>

      {message ? (
        <InlineNotice tone={messageType === "error" ? "error" : "success"} action={messageType === "success" ? <ViewJobButton jobId={lastCreatedJobId} disabled={isBusy} /> : undefined}>{message}</InlineNotice>
      ) : null}

      <fieldset className="permission-fieldset" disabled={!canOperate}>
      <div className="job-tool-grid job-excel-grid">
        <div className="job-tool-stack">
          <div className="job-tool-stack-title">
            <Download size={16} aria-hidden="true" />
            <strong>导入模板</strong>
          </div>
          <button className="command-button secondary" type="button" disabled={!canExportTemplate} onClick={handleExportTemplate}>
            <Download size={16} aria-hidden="true" />
            <span>导出导入模板</span>
          </button>
          {desktopAvailable ? <details className="job-tool-advanced">
            <summary>高级路径</summary>
            <div className="job-tool-advanced-content">
              <PathField
                label="模板输出"
                value={templateDestinationPath}
                disabled={isBusy}
                onChange={(value) => {
                  setTemplateDestinationPath(value);
                  setMessage(null);
                }}
                actions={
                  <>
                    {desktopAvailable ? (
                      <DesktopIconButton
                        title="选择模板保存位置"
                        disabled={isBusy}
                        onClick={() => pickExcelDestination("导入数据模板.xlsx", setTemplateDestinationPath)}
                      >
                        <Download size={15} aria-hidden="true" />
                      </DesktopIconButton>
                    ) : null}
                    {renderOpenPathAction(templateDestinationPath, "打开模板输出位置", showError)}
                  </>
                }
              />
              <button className="command-button secondary" type="button" disabled={!canExportTemplateByPath} onClick={handleExportTemplateByPath}>
                <Download size={16} aria-hidden="true" />
                <span>按路径导出</span>
              </button>
            </div>
          </details> : <span className="section-description">文件将保存到浏览器默认下载目录。</span>}
        </div>

        <div className="job-tool-stack">
          <div className="job-tool-stack-title">
            <Save size={16} aria-hidden="true" />
            <strong>空白托单</strong>
          </div>
          <button className="command-button secondary" type="button" disabled={!canExportBlankBooking} onClick={handleExportBlankBooking}>
            <Download size={16} aria-hidden="true" />
            <span>导出空白托单</span>
          </button>
          {desktopAvailable ? <details className="job-tool-advanced">
            <summary>高级路径</summary>
            <div className="job-tool-advanced-content">
              <PathField
                label="托单输出"
                value={blankBookingDestinationPath}
                disabled={isBusy}
                onChange={(value) => {
                  setBlankBookingDestinationPath(value);
                  setMessage(null);
                }}
                actions={
                  <>
                    {desktopAvailable ? (
                      <DesktopIconButton
                        title="选择空白托单保存位置"
                        disabled={isBusy}
                        onClick={() => pickExcelDestination("空白托单模板.xlsx", setBlankBookingDestinationPath)}
                      >
                        <Save size={15} aria-hidden="true" />
                      </DesktopIconButton>
                    ) : null}
                    {renderOpenPathAction(blankBookingDestinationPath, "打开空白托单输出位置", showError)}
                  </>
                }
              />
              <button
                className="command-button secondary"
                type="button"
                disabled={!canExportBlankBookingByPath}
                onClick={handleExportBlankBookingByPath}
              >
                <Download size={16} aria-hidden="true" />
                <span>按路径导出</span>
              </button>
            </div>
          </details> : <span className="section-description">文件将保存到浏览器默认下载目录。</span>}
        </div>

        <div className="job-tool-stack">
          <div className="job-tool-stack-title">
            <Play size={16} aria-hidden="true" />
            <strong>Excel 转托单</strong>
          </div>
          {!desktopAvailable ? (
            <label className="inline-filter">
              <span>选择 Excel</span>
              <input
                type="file"
                accept=".xlsx,.xlsm,.xltx,.xltm,.xls"
                disabled={isBusy}
                onChange={(event) => {
                  const file = event.currentTarget.files?.[0] ?? null;
                  event.currentTarget.value = "";
                  setConvertUploadFile(file);
                  setMessage(null);
                }}
              />
            </label>
          ) : null}
          <button className="command-button secondary" type="button" disabled={!canConvertBooking} onClick={handleConvertBooking}>
            <Play size={16} aria-hidden="true" />
            <span>选择 Excel 并转托单</span>
          </button>
          {desktopAvailable ? <details className="job-tool-advanced">
            <summary>高级路径</summary>
            <div className="job-tool-advanced-content">
              <PathField
                label="转换源"
                value={convertSourcePath}
                disabled={isBusy}
                onChange={(value) => {
                  setConvertSourcePath(value);
                  setMessage(null);
                }}
                actions={
                  desktopAvailable ? (
                    <DesktopIconButton title="选择已填写模板" disabled={isBusy} onClick={pickConvertSource}>
                      <FileSpreadsheet size={15} aria-hidden="true" />
                    </DesktopIconButton>
                  ) : undefined
                }
              />
              <PathField
                label="转换输出"
                value={convertDestinationPath}
                disabled={isBusy}
                onChange={(value) => {
                  setConvertDestinationPath(value);
                  setMessage(null);
                }}
                actions={
                  <>
                    {desktopAvailable ? (
                      <DesktopIconButton
                        title="选择转换保存位置"
                        disabled={isBusy}
                        onClick={() => pickExcelDestination(buildBookingSheetFileName(convertSourcePath), setConvertDestinationPath)}
                      >
                        <Save size={15} aria-hidden="true" />
                      </DesktopIconButton>
                    ) : null}
                    {renderOpenPathAction(convertDestinationPath, "打开转换输出位置", showError)}
                  </>
                }
              />
              <button className="command-button secondary" type="button" disabled={!canConvertBookingByPath} onClick={handleConvertBookingByPath}>
                <Play size={16} aria-hidden="true" />
                <span>按路径转换</span>
              </button>
            </div>
          </details> : <span className="section-description">上传文件只用于本次转换，完成后自动清理。</span>}
        </div>

        {canReadInvoices && <div className="job-tool-stack">
          <div className="job-tool-stack-title"><FileSpreadsheet size={16} aria-hidden="true" /><strong>已保存发票的托单</strong></div>
          <p className="section-description">从发票列表或单据预览中输出当前发票的托单。</p>
          <Link className="command-button secondary" to="/invoices">前往发票管理</Link>
        </div>}
      </div>
      </fieldset>
    </section>
  );
}

function buildBookingSheetFileName(sourcePath: string) {
  const baseName = sanitizeExcelFileBaseName(fileNameWithoutExtension(sourcePath));
  return `${baseName || "订舱托单"}_订舱托单.xlsx`;
}

function fileNameFromPath(value: string) {
  return value.split(/[\\/]/).filter(Boolean).at(-1) ?? value;
}

function fileNameWithoutExtension(value: string) {
  const fileName = fileNameFromPath(value.trim());
  const dotIndex = fileName.lastIndexOf(".");
  return dotIndex > 0 ? fileName.slice(0, dotIndex) : fileName;
}

function sanitizeExcelFileBaseName(value: string) {
  return value.trim().replace(/[<>:"/\\|?*\u0000-\u001f]+/g, "_").replace(/\s+/g, " ").slice(0, 80);
}
