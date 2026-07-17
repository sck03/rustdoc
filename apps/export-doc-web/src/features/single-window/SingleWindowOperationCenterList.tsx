import { useMutation,useQuery,useQueryClient } from "@tanstack/react-query";
import { Download,Files,FolderInput,FolderOpen,RefreshCw,Save,Send,Upload } from "lucide-react";
import { KeyboardEvent,useEffect,useState } from "react";
import {
ApiSingleWindowHandoffPackageResponse,
ApiSingleWindowImportedPackageResponse,
ExportDocManagerApiClient,
SingleWindowClientDispatchResult,
SingleWindowOperationCenterRow,
SingleWindowReceiptCollectionResult
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import {
isDesktopBridgeAvailable,
selectDirectory,
selectSavePackagePath
} from "../../desktop/desktopBridge.ts";
import { DesktopIconButton,readDesktopError,renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { PathField } from "../../ui/PathField.tsx";
import { formatPlainNumber,readApiError } from "../../ui/formUtils.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { readDefaultExportDirectory } from "../settings/settingsPaths.ts";

import { ClientDispatchResultDetail } from "./SingleWindowOperationCenterDetailPage.tsx";
import { DetailItem } from "./SingleWindowOperationCenterTables.tsx";
import {
ReceiptCollectionResultSummary,
ReceiptPackageExportResult,
ReceiptPackageImportResult,
} from "./SingleWindowReceiptResults.tsx";
import {
buildClientBoxPath,
buildReceiptPackageFileName,
formatBatchStatus,
formatBusinessType,
formatDateTime,
invalidateSingleWindowBatchQueries,
readDisplayText,
resolveBusinessClientRoot
} from "./singleWindowOperationCenterModel.ts";


export function FilterSelect({
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

export function OperationCenterTable({
  data,
  isBusy,
  selectedBatchId,
  onSelect,
  onOpen,
}: {
  data: SingleWindowOperationCenterRow[];
  isBusy: boolean;
  selectedBatchId?: number | null;
  onSelect: (batchId: number) => void;
  onOpen: (batchId: number) => void;
}) {
  function handleRowKeyDown(event: KeyboardEvent<HTMLTableRowElement>, batchId: number) {
    if (event.key === "Enter") {
      event.preventDefault();
      onOpen(batchId);
      return;
    }

    if (event.key === " ") {
      event.preventDefault();
      onSelect(batchId);
    }
  }

  return (
    <div className="table-frame" aria-busy={isBusy}>
      <table className="single-window-operation-table">
        <thead>
          <tr>
            <th>发票号</th>
            <th>合同号</th>
            <th>业务</th>
            <th>状态</th>
            <th>批次号</th>
            <th>版本</th>
            <th>回执</th>
            <th>客户端配置</th>
            <th>更新</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          {data.length === 0 ? (
            <tr>
              <td colSpan={10} className="empty-cell">
                {isBusy ? "加载中" : "暂无数据"}
              </td>
            </tr>
          ) : (
            data.map((row) => (
              <tr
                className={row.batchId === selectedBatchId ? "clickable-row selected-row" : "clickable-row"}
                key={row.batchId}
                tabIndex={0}
                aria-selected={row.batchId === selectedBatchId}
                onClick={() => onSelect(row.batchId)}
                onDoubleClick={() => onOpen(row.batchId)}
                onKeyDown={(event) => handleRowKeyDown(event, row.batchId)}
              >
                <td className="strong-cell">{readDisplayText(row.invoiceNo)}</td>
                <td>{readDisplayText(row.contractNo)}</td>
                <td>{formatBusinessType(row.businessType)}</td>
                <td>
                  <span className="status-pill">{formatBatchStatus(row.status)}</span>
                </td>
                <td>{readDisplayText(row.batchReference)}</td>
                <td>{`S${row.submissionVersion} / D${row.draftRevision}`}</td>
                <td title={row.lastReceiptMessage ?? ""}>
                  {row.receiptCount > 0 ? `${row.receiptCount} · ${readDisplayText(row.lastReceiptCode)}` : "0"}
                </td>
                <td>{readDisplayText(row.clientProfileName)}</td>
                <td>{formatDateTime(row.updatedAt)}</td>
                <td>
                  <button
                    className="icon-button compact-icon-button"
                    type="button"
                    title="打开批次详情"
                    onClick={(event) => {
                      event.stopPropagation();
                      onOpen(row.batchId);
                    }}
                  >
                    <Files size={15} aria-hidden="true" />
                  </button>
                </td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}

export function OperationCenterListActionsPanel({
  client,
  row,
  canOperate,
  onOpenDetail,
}: {
  client: ExportDocManagerApiClient;
  row: SingleWindowOperationCenterRow;
  canOperate: boolean;
  onOpenDetail: () => void;
}) {
  const queryClient = useQueryClient();
  const [directoryRootPath, setDirectoryRootPath] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [messageKind, setMessageKind] = useState<"success" | "error">("success");
  const [desktopMessage, setDesktopMessage] = useState<string | null>(null);
  const [dispatchResult, setDispatchResult] = useState<SingleWindowClientDispatchResult | null>(null);
  const [receiptCollectionResult, setReceiptCollectionResult] = useState<SingleWindowReceiptCollectionResult | null>(null);
  const [exportResult, setExportResult] = useState<ApiSingleWindowHandoffPackageResponse | null>(null);
  const [importResult, setImportResult] = useState<ApiSingleWindowImportedPackageResponse | null>(null);
  const [isAutoReceiptBusy, setIsAutoReceiptBusy] = useState(false);
  const isDesktop = isDesktopBridgeAvailable();

  const profileQuery = useQuery({
    queryKey: queryKeys.singleWindowClientProfile(),
    queryFn: () => client.getSingleWindowDefaultClientProfile(),
    staleTime: 60 * 1000,
  });

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: () => client.getSettings(),
    staleTime: 5 * 60 * 1000,
  });
  const defaultExportDirectory = readDefaultExportDirectory(settingsQuery.data?.settings);

  useEffect(() => {
    const profile = profileQuery.data?.profile;
    if (!profile) {
      return;
    }

    setDirectoryRootPath(
      resolveBusinessClientRoot(profile, row.businessType, "import") ||
        resolveBusinessClientRoot(profile, row.businessType, "receipt"),
    );
    setMessage(null);
    setDesktopMessage(null);
    setDispatchResult(null);
    setReceiptCollectionResult(null);
    setExportResult(null);
    setImportResult(null);
  }, [profileQuery.data, row.batchId, row.businessType]);

  const saveProfileMutation = useMutation({
    mutationFn: () =>
      client.saveSingleWindowDefaultClientProfile({
        body: {
          importRootPath: directoryRootPath.trim(),
          receiptRootPath: directoryRootPath.trim(),
          businessType: row.businessType,
        },
      }),
    onSuccess: async (response) => {
      setMessage(response.message || "当前业务目录根已保存。");
      setMessageKind("success");
      const nextRoot =
        resolveBusinessClientRoot(response.profile, row.businessType, "import") ||
        resolveBusinessClientRoot(response.profile, row.businessType, "receipt") ||
        directoryRootPath.trim();
      setDirectoryRootPath(nextRoot);
      queryClient.setQueryData(queryKeys.singleWindowClientProfile(), {
        profile: response.profile,
        storagePolicy: response.storagePolicy,
      });
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowClientProfile() });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setMessageKind("error");
    },
  });

  const dispatchMutation = useMutation({
    mutationFn: () =>
      client.dispatchSingleWindowBatchToClient({
        body: {
          batchId: row.batchId,
          importRootPath: directoryRootPath.trim() || undefined,
          profileName: profileQuery.data?.profile.profileName || "",
        },
      }),
    onSuccess: async (response) => {
      setDispatchResult(response);
      setMessage("当前批次已发送到默认导入目录。");
      setMessageKind("success");
      await invalidateSingleWindowBatchQueries(queryClient, row.batchId);
    },
    onError: (error) => {
      setDispatchResult(null);
      setMessage(readApiError(error));
      setMessageKind("error");
    },
  });

  const outBoxPath = buildClientBoxPath(directoryRootPath, "OutBox");
  const inBoxPath = buildClientBoxPath(directoryRootPath, "InBox");
  const failBoxPath = buildClientBoxPath(directoryRootPath, "FailBox");
  const isProfileBusy = profileQuery.isFetching || saveProfileMutation.isPending;
  const isDispatchBusy = dispatchMutation.isPending;
  const isBusy = isProfileBusy || isDispatchBusy || isAutoReceiptBusy;

  function saveDirectoryRoot() {
    if (!canOperate) return;

    setMessage(null);
    setDesktopMessage(null);
    if (!directoryRootPath.trim()) {
      setMessage("当前业务目录根不能为空。");
      setMessageKind("error");
      return;
    }

    saveProfileMutation.mutate();
  }

  function dispatchToDefaultImportRoot() {
    if (!canOperate) return;

    setMessage(null);
    setDesktopMessage(null);
    if (!directoryRootPath.trim()) {
      setMessage("请先保存或填写当前业务目录根。");
      setMessageKind("error");
      return;
    }

    dispatchMutation.mutate();
  }

  async function chooseDirectoryRoot() {
    if (!canOperate) return;

    try {
      const selectedPath = await selectDirectory();
      if (selectedPath) {
        setDirectoryRootPath(selectedPath);
        setMessage(null);
        setDesktopMessage(null);
      }
    } catch (error) {
      setDesktopMessage(readDesktopError(error));
    }
  }

  async function collectAndExportReceiptPackage(importAfterExport = false) {
    if (!canOperate || isAutoReceiptBusy) {
      return;
    }

    setMessage(null);
    setDesktopMessage(null);
    setReceiptCollectionResult(null);
    setExportResult(null);
    setImportResult(null);
    setIsAutoReceiptBusy(true);
    try {
      const collection = await client.collectSingleWindowClientReceipts({
        body: {
          batchId: row.batchId,
          receiptRootPath: undefined,
        },
      });
      setReceiptCollectionResult(collection);
      await invalidateSingleWindowBatchQueries(queryClient, row.batchId);

      if (collection.receiptFiles.length === 0) {
        setMessage("默认回执目录未发现可打包的回执文件。");
        setMessageKind("error");
        return;
      }

      if (!isDesktop) {
        const blob = await client.downloadSingleWindowReceiptPackage({
          body: {
            businessType: row.businessType,
            batchReference: row.batchReference || undefined,
            invoiceNo: row.invoiceNo || undefined,
            packagePath: undefined,
            receiptFiles: collection.receiptFiles,
          },
        });
        downloadBlob(blob, buildReceiptPackageFileName(row));
        setMessage("回执包已交给浏览器下载。");
        setMessageKind("success");
        return;
      }

      const selectedPath = await selectSavePackagePath(buildReceiptPackageFileName(row), defaultExportDirectory);
      if (!selectedPath) {
        setMessage(`已收集 ${formatPlainNumber(collection.receiptFiles.length)} 个回执文件，未选择回执包保存位置。`);
        setMessageKind("success");
        return;
      }

      const response = await client.saveSingleWindowReceiptPackageToPath({
        body: {
          businessType: row.businessType,
          batchReference: row.batchReference || undefined,
          invoiceNo: row.invoiceNo || undefined,
          packagePath: selectedPath,
          receiptFiles: collection.receiptFiles,
        },
      });
      setExportResult(response);
      await invalidateSingleWindowBatchQueries(queryClient, row.batchId);

      if (importAfterExport) {
        const imported = await client.importSingleWindowReceiptPackage({
          body: {
            packagePath: response.packagePath || selectedPath,
            keepWorkingDirectory: false,
          },
        });
        setImportResult(imported);
        setMessage(`回执包已导出并导入，写入 ${formatPlainNumber(imported.persistedReceiptCount)} 条回执。`);
        setMessageKind("success");
        await invalidateSingleWindowBatchQueries(queryClient, row.batchId);
        if (imported.trackingBatchId && imported.trackingBatchId !== row.batchId) {
          await invalidateSingleWindowBatchQueries(queryClient, imported.trackingBatchId);
        }
        return;
      }

      setMessage(response.message || "回执包已导出。");
      setMessageKind("success");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : readDesktopError(error));
      setMessageKind("error");
    } finally {
      setIsAutoReceiptBusy(false);
    }
  }

  return (
    <section className="form-section operation-center-list-actions" aria-label="选中批次快捷操作">
      <div className="section-header">
        <div>
          <h2>批次快捷操作</h2>
          <span>{readDisplayText(row.batchReference)}</span>
        </div>
        <div className="toolbar-actions">
          <button
            className="icon-button"
            type="button"
            title="刷新客户端目录档案"
            disabled={isBusy}
            onClick={() => void profileQuery.refetch()}
          >
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          <button className="command-button secondary" type="button" disabled={isBusy} onClick={onOpenDetail}>
            <Files size={17} aria-hidden="true" />
            <span>批次详情</span>
          </button>
          {isDesktop ? <button className="command-button secondary" type="button" disabled={!canOperate || isProfileBusy} onClick={saveDirectoryRoot}>
            <Save size={17} aria-hidden="true" />
            <span>保存目录根</span>
          </button> : null}
          {isDesktop ? <button className="command-button secondary" type="button" disabled={!canOperate || isDispatchBusy} onClick={dispatchToDefaultImportRoot}>
            <Send size={17} aria-hidden="true" />
            <span>发送到 OutBox</span>
          </button> : null}
          <button className="command-button secondary" type="button" disabled={!canOperate || isAutoReceiptBusy} onClick={() => void collectAndExportReceiptPackage()}>
            {isDesktop ? <FolderInput size={17} aria-hidden="true" /> : <Download size={17} aria-hidden="true" />}
            <span>{isDesktop ? "自动收件打包" : "收集并下载"}</span>
          </button>
          {isDesktop ? <button className="command-button secondary" type="button" disabled={!canOperate || isAutoReceiptBusy} onClick={() => void collectAndExportReceiptPackage(true)}>
            <Upload size={17} aria-hidden="true" />
            <span>打包并导入</span>
          </button> : null}
        </div>
      </div>

      {!canOperate ? <div className="permission-readonly-notice">当前权限仅允许查看批次详情；目录保存、派发和回执处理已禁用。</div> : null}
      {profileQuery.isError ? <div className="alert">{readApiError(profileQuery.error)}</div> : null}
      {message ? <div className={messageKind === "error" ? "alert" : "success-alert"}>{message}</div> : null}
      {desktopMessage ? <div className="alert">{desktopMessage}</div> : null}

      <div className="operation-center-list-action-grid">
        {isDesktop ? <PathField
          label={`${formatBusinessType(row.businessType)}目录根`}
          value={directoryRootPath}
          disabled={!canOperate || isProfileBusy}
          actions={
            isDesktop ? (
              <>
                <DesktopIconButton title="选择业务目录根" disabled={!canOperate || isProfileBusy} onClick={chooseDirectoryRoot}>
                  <FolderOpen size={17} aria-hidden="true" />
                </DesktopIconButton>
                {renderOpenPathAction(directoryRootPath, "打开业务目录根", setDesktopMessage)}
              </>
            ) : undefined
          }
          onChange={(value) => {
            setDirectoryRootPath(value);
            setMessage(null);
            setDesktopMessage(null);
          }}
        /> : null}
        <DetailItem label="发票号" value={row.invoiceNo} />
        <DetailItem label="业务" value={formatBusinessType(row.businessType)} />
        <DetailItem label="状态" value={formatBatchStatus(row.status)} />
        {isDesktop ? <DetailItem label="OutBox" value={outBoxPath} actions={renderOpenPathAction(outBoxPath, "打开 OutBox", setDesktopMessage)} /> : null}
        {isDesktop ? <DetailItem label="InBox" value={inBoxPath} actions={renderOpenPathAction(inBoxPath, "打开 InBox", setDesktopMessage)} /> : null}
        {isDesktop ? <DetailItem label="FailBox" value={failBoxPath} actions={renderOpenPathAction(failBoxPath, "打开 FailBox", setDesktopMessage)} /> : null}
        {isDesktop ? <DetailItem
          label="提交包"
          value={row.submitPackagePath}
          wide
          actions={renderOpenPathAction(row.submitPackagePath, "打开提交包位置", setDesktopMessage)}
        /> : null}
        {isDesktop ? <DetailItem
          label="客户端导入"
          value={row.clientDispatchPath}
          wide
          actions={renderOpenPathAction(row.clientDispatchPath, "打开客户端导入目录", setDesktopMessage)}
        /> : null}
      </div>

      {dispatchResult ? <ClientDispatchResultDetail result={dispatchResult} onOpenError={setDesktopMessage} /> : null}
      {receiptCollectionResult ? <ReceiptCollectionResultSummary result={receiptCollectionResult} onOpenError={setDesktopMessage} /> : null}
      {exportResult ? <ReceiptPackageExportResult result={exportResult} onOpenError={setDesktopMessage} /> : null}
      {importResult ? <ReceiptPackageImportResult result={importResult} onOpenError={setDesktopMessage} /> : null}
    </section>
  );
}
