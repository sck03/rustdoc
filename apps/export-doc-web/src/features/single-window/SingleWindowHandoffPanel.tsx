import { useEffect, useState, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Boxes, FolderInput, FolderOpen, RefreshCw, Save, Send } from "lucide-react";
import {
  ApiSingleWindowClientProfileDto,
  ApiSingleWindowHandoffPackageResponse,
  ExportDocManagerApiClient,
  SingleWindowClientDispatchResult,
  SingleWindowReceiptCollectionResult,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, selectDirectory } from "../../desktop/desktopBridge.ts";
import { DesktopIconButton, readDesktopError, renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { TextField } from "../../ui/FormFields.tsx";
import { PathField } from "../../ui/PathField.tsx";
import { formatPlainNumber, readApiError } from "../../ui/formUtils.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";

type SingleWindowHandoffPanelProps = {
  businessType: "CustomsCoo" | "AgentConsignment";
  client: ExportDocManagerApiClient;
  invoiceId: number;
  canOperate: boolean;
};

export function SingleWindowHandoffPanel({ businessType, client, invoiceId, canOperate }: SingleWindowHandoffPanelProps) {
  const queryClient = useQueryClient();
  const [importRootPath, setImportRootPath] = useState("");
  const [receiptRootPath, setReceiptRootPath] = useState("");
  const [profileMessage, setProfileMessage] = useState<string | null>(null);
  const [packageMessage, setPackageMessage] = useState<string | null>(null);
  const [dispatchMessage, setDispatchMessage] = useState<string | null>(null);
  const [receiptMessage, setReceiptMessage] = useState<string | null>(null);
  const [desktopMessage, setDesktopMessage] = useState<string | null>(null);
  const [packageResult, setPackageResult] = useState<ApiSingleWindowHandoffPackageResponse | null>(null);
  const [dispatchResult, setDispatchResult] = useState<SingleWindowClientDispatchResult | null>(null);
  const [receiptCollectionResult, setReceiptCollectionResult] = useState<SingleWindowReceiptCollectionResult | null>(null);
  const isDesktop = isDesktopBridgeAvailable();

  const profileQuery = useQuery({
    queryKey: queryKeys.singleWindowClientProfile(),
    queryFn: () => client.getSingleWindowDefaultClientProfile(),
    staleTime: 60 * 1000,
  });

  useEffect(() => {
    if (profileQuery.data?.profile) {
      setImportRootPath(profileQuery.data.profile.importRootPath ?? "");
      setReceiptRootPath(profileQuery.data.profile.receiptRootPath ?? "");
      setProfileMessage(null);
    }
  }, [profileQuery.data]);

  const exportPackageMutation = useMutation({
    mutationFn: async () => {
      if (isDesktop) {
        const response = businessType === "CustomsCoo"
          ? await client.saveCustomsCooSubmitPackageToPath({ invoiceId, body: {} })
          : await client.saveAgentConsignmentSubmitPackageToPath({ invoiceId, body: {} });
        return { mode: "desktop" as const, response };
      }

      const blob = businessType === "CustomsCoo"
        ? await client.downloadCustomsCooSubmitPackage({ invoiceId })
        : await client.downloadAgentConsignmentSubmitPackage({ invoiceId });
      downloadBlob(blob, `${businessType === "CustomsCoo" ? "COO" : "ACD"}-${invoiceId}.swpkg`);
      return { mode: "browser" as const };
    },
    onSuccess: async (result) => {
      setPackageResult(result.mode === "desktop" ? result.response : null);
      setPackageMessage(result.mode === "desktop" ? (result.response.message || "提交包已生成。") : "提交包已交给浏览器下载。");
      setDispatchResult(null);
      setDispatchMessage(null);
      setReceiptCollectionResult(null);
      setReceiptMessage(null);
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() });
    },
    onError: (error) => {
      setPackageMessage(readApiError(error));
      setPackageResult(null);
    },
  });

  const saveProfileMutation = useMutation({
    mutationFn: () =>
      client.saveSingleWindowDefaultClientProfile({
        body: {
          importRootPath: clientDirectoryRootPath,
          receiptRootPath: clientDirectoryRootPath,
        },
      }),
    onSuccess: async (response) => {
      setProfileMessage(response.message || "当前业务目录根已保存。");
      setImportRootPath(response.profile.importRootPath ?? "");
      setReceiptRootPath(response.profile.receiptRootPath ?? "");
      queryClient.setQueryData(queryKeys.singleWindowClientProfile(), {
        profile: response.profile,
        storagePolicy: response.storagePolicy,
      });
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowClientProfile() });
    },
    onError: (error) => {
      setProfileMessage(readApiError(error));
    },
  });

  const dispatchMutation = useMutation({
    mutationFn: () =>
      client.dispatchSingleWindowBatchToClient({
        body: {
          batchId: packageResult?.trackingBatchId ?? 0,
          importRootPath: clientDirectoryRootPath || undefined,
          profileName: profileQuery.data?.profile.profileName || "",
        },
      }),
    onSuccess: async (response) => {
      setDispatchResult(response);
      setDispatchMessage("提交包已派发到客户端导入目录。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() });
    },
    onError: (error) => {
      setDispatchMessage(readApiError(error));
      setDispatchResult(null);
    },
  });

  const collectReceiptsMutation = useMutation({
    mutationFn: () =>
      client.collectSingleWindowClientReceipts({
        body: {
          batchId: packageResult?.trackingBatchId ?? 0,
          receiptRootPath: clientDirectoryRootPath || undefined,
        },
      }),
    onSuccess: async (response) => {
      setReceiptCollectionResult(response);
      setReceiptMessage(`已收集 ${formatPlainNumber(response.receiptFiles.length)} 个回执文件。`);
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() });
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterDetail(response.batchId) });
    },
    onError: (error) => {
      setReceiptMessage(readApiError(error));
      setReceiptCollectionResult(null);
    },
  });

  const profile = profileQuery.data?.profile ?? null;
  const clientDirectoryRootPath = importRootPath.trim() || receiptRootPath.trim();
  const clientOutBoxPath = buildClientBoxPath(clientDirectoryRootPath, "OutBox");
  const clientSentBoxPath = buildClientBoxPath(clientDirectoryRootPath, "SentBox");
  const clientInBoxPath = buildClientBoxPath(clientDirectoryRootPath, "InBox");
  const clientFailBoxPath = buildClientBoxPath(clientDirectoryRootPath, "FailBox");
  const isProfileBusy = profileQuery.isFetching || saveProfileMutation.isPending;
  const isPackageBusy = exportPackageMutation.isPending || dispatchMutation.isPending || collectReceiptsMutation.isPending;
  const canDispatch = Boolean(packageResult?.trackingBatchId);

  function saveProfile() {
    if (!canOperate) return;

    setProfileMessage(null);
    if (!clientDirectoryRootPath) {
      setProfileMessage("当前业务目录根不能为空。");
      return;
    }

    saveProfileMutation.mutate();
  }

  function exportPackage() {
    if (!canOperate) return;

    setPackageMessage(null);
    exportPackageMutation.mutate();
  }

  function dispatchToClient() {
    if (!canOperate) return;

    setDispatchMessage(null);
    dispatchMutation.mutate();
  }

  function collectReceipts() {
    if (!canOperate) return;

    setReceiptMessage(null);
    collectReceiptsMutation.mutate();
  }

  function setClientDirectoryRootPath(value: string) {
    setImportRootPath(value);
    setReceiptRootPath(value);
    setProfileMessage(null);
    setReceiptMessage(null);
    setDesktopMessage(null);
  }

  async function chooseClientDirectoryRootPath() {
    if (!canOperate) return;

    try {
      const selectedPath = await selectDirectory();
      if (selectedPath) {
        setClientDirectoryRootPath(selectedPath);
      }
    } catch (error) {
      setDesktopMessage(readDesktopError(error));
    }
  }

  if (!isDesktop) {
    return (
      <section className="form-section" aria-label="单一窗口提交包下载">
        <div className="section-header">
          <div>
            <h2>提交包下载</h2>
            <span>浏览器不会显示或写入服务器绝对路径</span>
          </div>
          <button className="command-button secondary" type="button" disabled={!canOperate || exportPackageMutation.isPending} onClick={exportPackage}>
            <Boxes size={17} aria-hidden="true" />
            <span>下载提交包</span>
          </button>
        </div>
        {packageMessage ? <InlineNotice tone={exportPackageMutation.isError ? "error" : "success"}>{packageMessage}</InlineNotice> : null}
      </section>
    );
  }

  return (
    <section className="form-section" aria-label="提交包与客户端目录">
      <div className="section-header">
        <h2>提交包与客户端目录</h2>
        <div className="toolbar-actions">
          <button
            className="icon-button"
            type="button"
            title="刷新客户端目录档案" aria-label="刷新客户端目录档案"
            disabled={!canOperate || isProfileBusy}
            onClick={() => void profileQuery.refetch()}
          >
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          <button className="command-button secondary" type="button" disabled={!canOperate || isPackageBusy} onClick={exportPackage}>
            <Boxes size={17} aria-hidden="true" />
            <span>生成提交包</span>
          </button>
          <button
            className="command-button secondary"
            type="button"
            disabled={!canOperate || isPackageBusy || !canDispatch}
            onClick={dispatchToClient}
          >
            <Send size={17} aria-hidden="true" />
            <span>发送到 OutBox</span>
          </button>
          <button
            className="command-button secondary"
            type="button"
            disabled={!canOperate || isPackageBusy || !canDispatch}
            onClick={collectReceipts}
          >
            <FolderInput size={17} aria-hidden="true" />
            <span>从 InBox 收集</span>
          </button>
        </div>
      </div>

      {!canOperate ? <PermissionNotice>当前权限仅允许查看客户端目录档案；生成、派发、收集和保存目录已禁用。</PermissionNotice> : null}
      {profileQuery.isError ? <InlineNotice tone="error" title="交接配置加载失败">{readApiError(profileQuery.error)}</InlineNotice> : null}
      {profileMessage ? <InlineNotice tone={saveProfileMutation.isError ? "error" : "success"}>{profileMessage}</InlineNotice> : null}
      {packageMessage ? <InlineNotice tone={exportPackageMutation.isError ? "error" : "success"}>{packageMessage}</InlineNotice> : null}
      {dispatchMessage ? <InlineNotice tone={dispatchMutation.isError ? "error" : "success"}>{dispatchMessage}</InlineNotice> : null}
      {receiptMessage ? (
        <InlineNotice tone={collectReceiptsMutation.isError ? "error" : "success"}>{receiptMessage}</InlineNotice>
      ) : null}
      {desktopMessage ? <InlineNotice tone="error" title="交接包操作失败">{desktopMessage}</InlineNotice> : null}

      <div className="field-grid">
        <PathField
          label="当前业务目录根"
          value={clientDirectoryRootPath}
          disabled={!canOperate || isProfileBusy}
          actions={
            isDesktop ? (
              <>
                <DesktopIconButton title="选择当前业务目录根" disabled={!canOperate || isProfileBusy} onClick={chooseClientDirectoryRootPath}>
                  <FolderOpen size={17} aria-hidden="true" />
                </DesktopIconButton>
                {renderOpenPathAction(clientDirectoryRootPath, "打开当前业务目录根", setDesktopMessage)}
              </>
            ) : undefined
          }
          onChange={setClientDirectoryRootPath}
        />
        <ReadOnlyProfileField label="档案名称" value={profile?.profileName} />
        <ReadOnlyProfileField label="机器名" value={profile?.machineName} />
      </div>

      <div className="single-window-client-folder-grid" aria-label="官方单一窗口客户端目录">
        <DetailItem
          label="待发送 OutBox"
          value={clientOutBoxPath}
          actions={renderOpenPathAction(clientOutBoxPath, "打开 OutBox", setDesktopMessage)}
        />
        <DetailItem
          label="已发送 SentBox"
          value={clientSentBoxPath}
          actions={renderOpenPathAction(clientSentBoxPath, "打开 SentBox", setDesktopMessage)}
        />
        <DetailItem
          label="回执/异常 InBox"
          value={clientInBoxPath}
          actions={renderOpenPathAction(clientInBoxPath, "打开 InBox", setDesktopMessage)}
        />
        <DetailItem
          label="校验失败 FailBox"
          value={clientFailBoxPath}
          actions={renderOpenPathAction(clientFailBoxPath, "打开 FailBox", setDesktopMessage)}
        />
      </div>

      <div className="toolbar-actions handoff-profile-actions">
        <button className="command-button" type="button" disabled={!canOperate || isProfileBusy} onClick={saveProfile}>
          <Save size={17} aria-hidden="true" />
          <span>保存目录档案</span>
        </button>
      </div>

      {packageResult ? <PackageResultDetail result={packageResult} onOpenError={setDesktopMessage} /> : null}
      {dispatchResult ? <DispatchResultDetail result={dispatchResult} onOpenError={setDesktopMessage} /> : null}
      {receiptCollectionResult ? (
        <ReceiptCollectionResultDetail result={receiptCollectionResult} onOpenError={setDesktopMessage} />
      ) : null}
    </section>
  );
}

function ReadOnlyProfileField({ label, value }: { label: string; value?: string }) {
  return <TextField label={label} value={value ?? ""} disabled onChange={() => undefined} />;
}

function buildClientBoxPath(rootPath: string, boxName: "OutBox" | "SentBox" | "InBox" | "FailBox") {
  const normalizedRoot = rootPath.trim().replace(/[\\/]+$/, "");
  if (!normalizedRoot) {
    return "";
  }

  const separator = normalizedRoot.includes("/") && !normalizedRoot.includes("\\") ? "/" : "\\";
  return `${normalizedRoot}${separator}${boxName}`;
}

function PackageResultDetail({
  result,
  onOpenError,
}: {
  result: ApiSingleWindowHandoffPackageResponse;
  onOpenError: (message: string) => void;
}) {
  const manifest = result.manifest;

  return (
    <div className="detail-grid handoff-result-grid">
      <DetailItem label="批次 ID" value={result.trackingBatchId ?? "-"} />
      <DetailItem label="批次号" value={manifest.batchReference} />
      <DetailItem label="提交版本" value={manifest.submissionVersion} />
      <DetailItem label="草稿版本" value={manifest.draftRevision} />
      <DetailItem label="负载文件" value={manifest.payloadFiles.length} />
      <DetailItem label="附件文件" value={manifest.attachmentFiles.length} />
      <DetailItem label="警告" value={manifest.warnings.length} />
      <DetailItem
        label="包路径"
        value={result.packagePath}
        wide
        actions={renderOpenPathAction(result.packagePath, "打开提交包位置", onOpenError)}
      />
    </div>
  );
}

function DispatchResultDetail({
  result,
  onOpenError,
}: {
  result: SingleWindowClientDispatchResult;
  onOpenError: (message: string) => void;
}) {
  return (
    <div className="detail-grid handoff-result-grid">
      <DetailItem label="批次 ID" value={result.batchId} />
      <DetailItem label="批次号" value={result.batchReference} />
      <DetailItem label="客户端档案" value={result.profileName} />
      <DetailItem label="负载文件" value={result.payloadFileCount} />
      <DetailItem label="附件文件" value={result.attachmentFileCount} />
      <DetailItem
        label="目标目录"
        value={result.targetDirectory}
        wide
        actions={renderOpenPathAction(result.targetDirectory, "打开目标目录", onOpenError)}
      />
    </div>
  );
}

function ReceiptCollectionResultDetail({
  result,
  onOpenError,
}: {
  result: SingleWindowReceiptCollectionResult;
  onOpenError: (message: string) => void;
}) {
  return (
    <>
      <div className="detail-grid handoff-result-grid">
        <DetailItem label="批次 ID" value={result.batchId} />
        <DetailItem label="批次号" value={result.batchReference} />
        <DetailItem label="回执文件" value={result.receiptFiles.length} />
        <DetailItem
          label="回执根目录"
          value={result.receiptRootPath}
          wide
          actions={renderOpenPathAction(result.receiptRootPath, "打开回执根目录", onOpenError)}
        />
      </div>
      <ResponsiveTableFrame className="compact-table handoff-receipt-files" label="单一窗口回执文件">
        <table>
          <thead>
            <tr>
              <th>序号</th>
              <th>回执文件</th>
            </tr>
          </thead>
          <tbody>
            {result.receiptFiles.length > 0 ? (
              result.receiptFiles.map((filePath, index) => (
                <tr key={`${filePath}-${index}`}>
                  <td>{formatPlainNumber(index + 1)}</td>
                  <td className="path-cell" title={filePath}>
                    {filePath}
                  </td>
                </tr>
              ))
            ) : (
              <tr>
                <td className="empty-cell" colSpan={2}>
                  未发现可收集的回执文件。
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </ResponsiveTableFrame>
    </>
  );
}

function DetailItem({
  label,
  value,
  wide,
  actions,
}: {
  label: string;
  value?: string | number;
  wide?: boolean;
  actions?: ReactNode;
}) {
  const displayValue = typeof value === "number" ? formatPlainNumber(value) : value?.trim() ? value : "-";

  return (
    <div className={wide ? "detail-item detail-item-wide" : "detail-item"}>
      <span>{label}</span>
      {actions ? (
        <div className="detail-value-row">
          <strong title={displayValue}>{displayValue}</strong>
          <div className="detail-item-actions">{actions}</div>
        </div>
      ) : (
        <strong title={displayValue}>{displayValue}</strong>
      )}
    </div>
  );
}
