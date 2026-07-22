import { useMutation,useQuery,useQueryClient } from "@tanstack/react-query";
import { Download,FileInput,FileOutput,Files,FolderInput,FolderOpen,Upload } from "lucide-react";
import { useEffect,useState } from "react";
import {
ApiSingleWindowHandoffPackageResponse,
ApiSingleWindowImportedPackageResponse,
ExportDocManagerApiClient,
SingleWindowOperationCenterDetail,
SingleWindowReceiptCollectionResult
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import {
isDesktopBridgeAvailable,
selectDirectory,
selectReceiptFiles,
selectSavePackagePath,
selectSingleWindowPackageFile,
} from "../../desktop/desktopBridge.ts";
import { DesktopIconButton,readDesktopError,renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { PathField,PathTextAreaField } from "../../ui/PathField.tsx";
import { formatPlainNumber,readApiError } from "../../ui/formUtils.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { readDefaultExportDirectory } from "../settings/settingsPaths.ts";

import {
buildReceiptPackageFileName,
mergePathLines,
parseReceiptFilePaths
} from "./singleWindowOperationCenterModel.ts";

import {
ReceiptCollectionResultSummary,
ReceiptPackageExportResult,
ReceiptPackageImportResult,
} from "./SingleWindowReceiptResults.tsx";

export function ReceiptPackagePanel({
  client,
  detail,
  canOperate,
}: {
  client: ExportDocManagerApiClient;
  detail: SingleWindowOperationCenterDetail;
  canOperate: boolean;
}) {
  const queryClient = useQueryClient();
  const [receiptFilesText, setReceiptFilesText] = useState("");
  const [exportPackagePath, setExportPackagePath] = useState("");
  const [importPackagePath, setImportPackagePath] = useState(detail.lastReceiptPackagePath ?? "");
  const [importWorkingDirectory, setImportWorkingDirectory] = useState("");
  const [receiptUploadFile, setReceiptUploadFile] = useState<File | null>(null);
  const [keepWorkingDirectory, setKeepWorkingDirectory] = useState(false);
  const [exportMessage, setExportMessage] = useState<string | null>(null);
  const [exportMessageKind, setExportMessageKind] = useState<"success" | "error">("success");
  const [importMessage, setImportMessage] = useState<string | null>(null);
  const [desktopMessage, setDesktopMessage] = useState<string | null>(null);
  const [exportResult, setExportResult] = useState<ApiSingleWindowHandoffPackageResponse | null>(null);
  const [importResult, setImportResult] = useState<ApiSingleWindowImportedPackageResponse | null>(null);
  const [receiptCollectionResult, setReceiptCollectionResult] = useState<SingleWindowReceiptCollectionResult | null>(null);
  const [isDefaultExportBusy, setIsDefaultExportBusy] = useState(false);
  const isDesktop = isDesktopBridgeAvailable();

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: () => client.getSettings(),
    staleTime: 5 * 60 * 1000,
  });
  const defaultExportDirectory = readDefaultExportDirectory(settingsQuery.data?.settings);

  useEffect(() => {
    setImportPackagePath(detail.lastReceiptPackagePath ?? "");
  }, [detail.lastReceiptPackagePath]);

  const exportReceiptPackageMutation = useMutation({
    mutationFn: async () => {
      const body = {
        businessType: detail.businessType,
        batchReference: detail.batchReference || undefined,
        invoiceNo: detail.invoiceNo || undefined,
        packagePath: isDesktop ? (exportPackagePath.trim() || undefined) : undefined,
        receiptFiles: parseReceiptFilePaths(receiptFilesText),
      };
      if (isDesktop) {
        const response = await client.saveSingleWindowReceiptPackageToPath({ body });
        return { mode: "desktop" as const, response };
      }

      const blob = await client.downloadSingleWindowReceiptPackage({ body });
      downloadBlob(blob, buildReceiptPackageFileName(detail));
      return { mode: "browser" as const };
    },
    onSuccess: async (result) => {
      setExportResult(result.mode === "desktop" ? result.response : null);
      setExportMessage(result.mode === "desktop" ? (result.response.message || "回执包已导出。") : "回执包已交给浏览器下载。");
      setExportMessageKind("success");
      if (result.mode === "desktop") {
        setExportPackagePath(result.response.packagePath ?? "");
        setImportPackagePath(result.response.packagePath ?? "");
      }
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() });
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterDetail(detail.batchId) });
    },
    onError: (error) => {
      setExportResult(null);
      setExportMessage(readApiError(error));
      setExportMessageKind("error");
    },
  });

  const collectReceiptsMutation = useMutation({
    mutationFn: () =>
      client.collectSingleWindowClientReceipts({
        body: {
          batchId: detail.batchId,
          receiptRootPath: undefined,
        },
      }),
    onSuccess: async (response) => {
      setReceiptCollectionResult(response);
      setReceiptFilesText(mergePathLines("", response.receiptFiles));
      setExportMessage(`已从默认回执目录收集 ${formatPlainNumber(response.receiptFiles.length)} 个回执文件。`);
      setExportMessageKind(response.receiptFiles.length > 0 ? "success" : "error");
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() });
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterDetail(detail.batchId) });
    },
    onError: (error) => {
      setReceiptCollectionResult(null);
      setExportMessage(readApiError(error));
      setExportMessageKind("error");
    },
  });

  const importReceiptPackageMutation = useMutation({
    mutationFn: () => isDesktop
      ? client.importSingleWindowReceiptPackage({
        body: {
          packagePath: importPackagePath.trim(),
          workingDirectory: importWorkingDirectory.trim() || undefined,
          keepWorkingDirectory,
        },
      })
      : client.uploadSingleWindowReceiptPackage({
          fileName: receiptUploadFile?.name,
          keepWorkingDirectory: false,
          body: receiptUploadFile ?? new Blob(),
        }),
    onSuccess: async (response) => {
      setImportResult(response);
      setImportMessage(response.message || "回执包已导入。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() });
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterDetail(detail.batchId) });
      if (response.trackingBatchId && response.trackingBatchId !== detail.batchId) {
        await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterDetail(response.trackingBatchId) });
      }
    },
    onError: (error) => {
      setImportResult(null);
      setImportMessage(readApiError(error));
    },
  });

  const isExportBusy = exportReceiptPackageMutation.isPending || collectReceiptsMutation.isPending || isDefaultExportBusy;
  const isImportBusy = importReceiptPackageMutation.isPending;

  function exportReceiptPackage() {
    if (!canOperate) return;

    const receiptFiles = parseReceiptFilePaths(receiptFilesText);
    setExportMessage(null);
    if (receiptFiles.length === 0) {
      setExportResult(null);
      setExportMessage("回执文件列表不能为空。");
      setExportMessageKind("error");
      return;
    }

    exportReceiptPackageMutation.mutate();
  }

  function collectReceiptsFromDefaultRoot() {
    if (!canOperate) return;

    setExportMessage(null);
    setDesktopMessage(null);
    collectReceiptsMutation.mutate();
  }

  async function exportReceiptPackageFromDefaultRoot() {
    if (!canOperate) return;

    setExportMessage(null);
    setDesktopMessage(null);
    setIsDefaultExportBusy(true);

    try {
      const collection = await client.collectSingleWindowClientReceipts({
        body: {
          batchId: detail.batchId,
          receiptRootPath: undefined,
        },
      });
      setReceiptCollectionResult(collection);
      setReceiptFilesText(mergePathLines("", collection.receiptFiles));

      if (collection.receiptFiles.length === 0) {
        setExportResult(null);
        setExportMessage("默认回执目录中未找到匹配当前批次的回执文件。");
        setExportMessageKind("error");
        return;
      }

      if (!isDesktop) {
        const blob = await client.downloadSingleWindowReceiptPackage({
          body: {
            businessType: detail.businessType,
            batchReference: detail.batchReference || undefined,
            invoiceNo: detail.invoiceNo || undefined,
            packagePath: undefined,
            receiptFiles: collection.receiptFiles,
          },
        });
        downloadBlob(blob, buildReceiptPackageFileName(detail));
        setExportResult(null);
        setExportMessage("回执包已交给浏览器下载。");
        setExportMessageKind("success");
        return;
      }

      let packagePath = exportPackagePath.trim();
      if (!packagePath && isDesktop) {
        const selectedPath = await selectSavePackagePath(buildReceiptPackageFileName(detail), defaultExportDirectory);
        if (selectedPath) {
          packagePath = selectedPath;
          setExportPackagePath(selectedPath);
        }
      }

      if (!packagePath) {
        setExportResult(null);
        setExportMessage(
          isDesktop
            ? "已收集回执文件，请选择回执包保存路径后再导出。"
            : "已收集回执文件，请填写回执包保存路径后再导出。",
        );
        setExportMessageKind("error");
        return;
      }

      const response = await client.saveSingleWindowReceiptPackageToPath({
        body: {
          businessType: detail.businessType,
          batchReference: detail.batchReference || undefined,
          invoiceNo: detail.invoiceNo || undefined,
          packagePath,
          receiptFiles: collection.receiptFiles,
        },
      });

      setExportResult(response);
      setExportMessage(response.message || "已从默认回执目录导出回执包。");
      setExportMessageKind("success");
      setExportPackagePath(response.packagePath ?? packagePath);
      setImportPackagePath(response.packagePath ?? packagePath);
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() });
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterDetail(detail.batchId) });
    } catch (error) {
      setExportResult(null);
      setExportMessage(readApiError(error));
      setExportMessageKind("error");
    } finally {
      setIsDefaultExportBusy(false);
    }
  }

  function importReceiptPackage() {
    if (!canOperate) return;

    setImportMessage(null);
    if (isDesktop ? !importPackagePath.trim() : !receiptUploadFile) {
      setImportResult(null);
      setImportMessage(isDesktop ? "回执包路径不能为空。" : "请选择要上传的回执包。");
      return;
    }

    importReceiptPackageMutation.mutate();
  }

  async function chooseReceiptFiles() {
    if (!canOperate) return;

    try {
      const selectedPaths = await selectReceiptFiles();
      if (selectedPaths.length > 0) {
        setReceiptFilesText(mergePathLines(receiptFilesText, selectedPaths));
        setExportMessage(null);
        setDesktopMessage(null);
      }
    } catch (error) {
      setDesktopMessage(readDesktopError(error));
    }
  }

  async function chooseExportPackagePath() {
    if (!canOperate) return;

    try {
      const selectedPath = await selectSavePackagePath(buildReceiptPackageFileName(detail), defaultExportDirectory);
      if (selectedPath) {
        setExportPackagePath(selectedPath);
        setExportMessage(null);
        setDesktopMessage(null);
      }
    } catch (error) {
      setDesktopMessage(readDesktopError(error));
    }
  }

  async function chooseImportPackagePath() {
    if (!canOperate) return;

    try {
      const selectedPath = await selectSingleWindowPackageFile();
      if (selectedPath) {
        setImportPackagePath(selectedPath);
        setImportMessage(null);
        setDesktopMessage(null);
      }
    } catch (error) {
      setDesktopMessage(readDesktopError(error));
    }
  }

  async function chooseImportWorkingDirectory() {
    if (!canOperate) return;

    try {
      const selectedPath = await selectDirectory();
      if (selectedPath) {
        setImportWorkingDirectory(selectedPath);
        setImportMessage(null);
        setDesktopMessage(null);
      }
    } catch (error) {
      setDesktopMessage(readDesktopError(error));
    }
  }

  if (!isDesktop) {
    return (
      <section className="form-section" aria-label="回执包下载">
        <div className="section-header">
          <div>
            <h2>回执包下载</h2>
            <span>从受控客户端目录收集回执并下载，不显示服务器路径</span>
          </div>
          <div className="toolbar-actions">
            <button className="command-button secondary" type="button" disabled={!canOperate || isExportBusy} onClick={collectReceiptsFromDefaultRoot}>
              <FolderInput size={17} aria-hidden="true" />
              <span>收集回执</span>
            </button>
            <button className="command-button secondary" type="button" disabled={!canOperate || isExportBusy} onClick={exportReceiptPackageFromDefaultRoot}>
              <Download size={17} aria-hidden="true" />
              <span>收集并下载</span>
            </button>
          </div>
        </div>
        {exportMessage ? <InlineNotice tone={exportMessageKind === "error" ? "error" : "success"}>{exportMessage}</InlineNotice> : null}
        {receiptCollectionResult ? <InlineNotice tone="info">已收集 {formatPlainNumber(receiptCollectionResult.receiptFiles.length)} 个回执文件。</InlineNotice> : null}
        <div className="job-tool-submit-row">
          <label className="inline-filter"><span>导入回执包</span><input type="file" accept=".swpkg" disabled={!canOperate || isImportBusy} onChange={(event) => { setReceiptUploadFile(event.target.files?.[0] ?? null); setImportMessage(null); }} /></label>
          <button className="command-button secondary" type="button" disabled={!canOperate || isImportBusy || !receiptUploadFile} onClick={importReceiptPackage}>
            <Upload size={17} aria-hidden="true" />
            <span>上传并导入</span>
          </button>
        </div>
        {importMessage ? <InlineNotice tone={importReceiptPackageMutation.isError || !importResult ? "error" : "success"}>{importMessage}</InlineNotice> : null}
      </section>
    );
  }

  return (
    <section className="form-section" aria-label="回执包导入导出">
      <div className="section-header">
        <h2>回执包导入导出</h2>
      </div>

      {exportMessage ? (
        <InlineNotice tone={exportMessageKind === "error" ? "error" : "success"}>
          {exportMessage}
        </InlineNotice>
      ) : null}
      {importMessage ? (
        <InlineNotice tone={importReceiptPackageMutation.isError || !importResult ? "error" : "success"}>
          {importMessage}
        </InlineNotice>
      ) : null}
      {desktopMessage ? <InlineNotice tone="error" title="回执包操作失败">{desktopMessage}</InlineNotice> : null}
      {!canOperate ? (
        <PermissionNotice>
          当前权限仅允许查看回执处理记录；收集、打包、导入及目录修改已禁用。
        </PermissionNotice>
      ) : null}

      <div className="receipt-package-grid">
        <section className="receipt-package-panel" aria-label="导出回执包">
          <div className="section-header">
            <h3>导出回执包</h3>
            <div className="toolbar-actions">
              <button className="command-button secondary" type="button" disabled={!canOperate || isExportBusy} onClick={collectReceiptsFromDefaultRoot}>
                <FolderInput size={17} aria-hidden="true" />
                <span>收集回执</span>
              </button>
              <button className="command-button secondary" type="button" disabled={!canOperate || isExportBusy} onClick={exportReceiptPackageFromDefaultRoot}>
                <Download size={17} aria-hidden="true" />
                <span>默认目录打包</span>
              </button>
              <button className="command-button secondary" type="button" disabled={!canOperate || isExportBusy} onClick={exportReceiptPackage}>
                <Download size={17} aria-hidden="true" />
                <span>导出回执包</span>
              </button>
            </div>
          </div>
          <PathTextAreaField
            label="回执文件路径"
            value={receiptFilesText}
            disabled={!canOperate || isExportBusy}
            actions={
              isDesktop ? (
                <DesktopIconButton title="选择回执文件" disabled={!canOperate || isExportBusy} onClick={chooseReceiptFiles}>
                  <Files size={17} aria-hidden="true" />
                </DesktopIconButton>
              ) : undefined
            }
            onChange={(value) => {
              setReceiptFilesText(value);
              setExportMessage(null);
              setDesktopMessage(null);
            }}
          />
          <PathField
            label="回执包保存路径"
            value={exportPackagePath}
            disabled={!canOperate || isExportBusy}
            actions={
              isDesktop ? (
                <>
                  <DesktopIconButton title="选择保存位置" disabled={!canOperate || isExportBusy} onClick={chooseExportPackagePath}>
                    <FileOutput size={17} aria-hidden="true" />
                  </DesktopIconButton>
                  {renderOpenPathAction(exportPackagePath, "打开回执包位置", setDesktopMessage)}
                </>
              ) : undefined
            }
            onChange={(value) => {
              setExportPackagePath(value);
              setExportMessage(null);
              setDesktopMessage(null);
            }}
          />
        </section>

        <section className="receipt-package-panel" aria-label="导入回执包">
          <div className="section-header">
            <h3>导入回执包</h3>
            <button className="command-button secondary" type="button" disabled={!canOperate || isImportBusy} onClick={importReceiptPackage}>
              <Upload size={17} aria-hidden="true" />
              <span>导入回执包</span>
            </button>
          </div>
          <PathField
            label="回执包路径"
            value={importPackagePath}
            disabled={!canOperate || isImportBusy}
            actions={
              isDesktop ? (
                <>
                  <DesktopIconButton title="选择回执包" disabled={!canOperate || isImportBusy} onClick={chooseImportPackagePath}>
                    <FileInput size={17} aria-hidden="true" />
                  </DesktopIconButton>
                  {renderOpenPathAction(importPackagePath, "打开回执包位置", setDesktopMessage)}
                </>
              ) : undefined
            }
            onChange={(value) => {
              setImportPackagePath(value);
              setImportMessage(null);
              setDesktopMessage(null);
            }}
          />
          <PathField
            label="导入工作目录"
            value={importWorkingDirectory}
            disabled={!canOperate || isImportBusy}
            actions={
              isDesktop ? (
                <>
                  <DesktopIconButton title="选择工作目录" disabled={!canOperate || isImportBusy} onClick={chooseImportWorkingDirectory}>
                    <FolderOpen size={17} aria-hidden="true" />
                  </DesktopIconButton>
                  {renderOpenPathAction(importWorkingDirectory, "打开工作目录", setDesktopMessage)}
                </>
              ) : undefined
            }
            onChange={(value) => {
              setImportWorkingDirectory(value);
              setImportMessage(null);
              setDesktopMessage(null);
            }}
          />
          <label className="inline-check receipt-package-keep">
            <input
              type="checkbox"
              checked={keepWorkingDirectory}
              disabled={!canOperate || isImportBusy}
              onChange={(event) => setKeepWorkingDirectory(event.target.checked)}
            />
            <span>保留工作目录</span>
          </label>
        </section>
      </div>

      {receiptCollectionResult ? <ReceiptCollectionResultSummary result={receiptCollectionResult} onOpenError={setDesktopMessage} /> : null}
      {exportResult ? <ReceiptPackageExportResult result={exportResult} onOpenError={setDesktopMessage} /> : null}
      {importResult ? <ReceiptPackageImportResult result={importResult} onOpenError={setDesktopMessage} /> : null}
    </section>
  );
}
