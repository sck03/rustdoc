import { useMutation,useQuery,useQueryClient } from "@tanstack/react-query";
import { ArrowLeft,Boxes,FolderOpen,RefreshCw,Save,Send } from "lucide-react";
import { useEffect,useState } from "react";
import { useNavigate,useParams } from "react-router-dom";
import {
ExportDocManagerApiClient,
SingleWindowClientDispatchResult,
SingleWindowOperationCenterDetail
} from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { queryKeys } from "../../api/queryKeys.ts";
import {
isDesktopBridgeAvailable,
selectDirectory
} from "../../desktop/desktopBridge.ts";
import { DesktopIconButton,readDesktopError,renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { PathField } from "../../ui/PathField.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice, PageState, PermissionNotice } from "../../ui/PageState.tsx";

import {
buildClientBoxPath,
formatBatchStatus,
formatBusinessType,
formatDateTime,
resolveBusinessClientRoot
} from "./singleWindowOperationCenterModel.ts";

import { DetailItem,PackageRecordTable,ReceiptRecordTable } from "./SingleWindowOperationCenterTables.tsx";
import { ReceiptPackagePanel } from "./SingleWindowReceiptPackagePanel.tsx";

export function SingleWindowOperationCenterDetailPage({ client }: { client: ExportDocManagerApiClient }) {
  const permission = useModulePermission("document.single-window");
  const { batchId } = useParams();
  const navigate = useNavigate();
  const parsedBatchId = Number(batchId);
  const isBatchIdValid = Number.isInteger(parsedBatchId) && parsedBatchId > 0;

  const detailQuery = useQuery({
    queryKey: queryKeys.singleWindowOperationCenterDetail(parsedBatchId),
    queryFn: () => client.getSingleWindowOperationCenterDetail({ batchId: parsedBatchId }),
    enabled: isBatchIdValid,
  });

  const detail = detailQuery.data ?? null;
  const message = !isBatchIdValid
    ? "批次 ID 无效。"
    : detailQuery.isError
      ? readApiError(detailQuery.error)
      : null;

  return (
    <section className="editor-surface single-window-detail-surface" aria-label="单一窗口批次详情">
      <div className="editor-toolbar">
        <button className="command-button secondary" type="button" onClick={() => navigate("/single-window/operation-center")}>
          <ArrowLeft size={17} aria-hidden="true" />
          <span>返回列表</span>
        </button>
        <div className="editor-title">
          <Boxes size={18} aria-hidden="true" />
          <span>{detail ? detail.batchReference || `批次 ${detail.batchId}` : "批次详情"}</span>
        </div>
      </div>

      {message ? <InlineNotice tone="error" title="批次详情加载失败">{message}</InlineNotice> : null}
      {!permission.canOperate ? <PermissionNotice>当前权限模板仅允许查看批次、包和回执记录；客户端派发及回执处理已禁用。</PermissionNotice> : null}
      {!detail && detailQuery.isFetching ? <PageState tone="loading" title="正在加载批次详情" description="请稍候，系统正在读取提交包和回执记录。" /> : null}

      {detail ? <OperationCenterDetail client={client} detail={detail} canOperate={permission.canOperate} /> : null}
    </section>
  );
}


export function OperationCenterDetail({
  client,
  detail,
  canOperate,
}: {
  client: ExportDocManagerApiClient;
  detail: SingleWindowOperationCenterDetail;
  canOperate: boolean;
}) {
  const [desktopMessage, setDesktopMessage] = useState<string | null>(null);

  return (
    <div className="entity-form">
      {desktopMessage ? <InlineNotice tone="error" title="桌面文件操作失败">{desktopMessage}</InlineNotice> : null}
      <section className="form-section" aria-label="批次信息">
        <div className="section-header">
          <h2>批次信息</h2>
        </div>
        <div className="detail-grid">
          <DetailItem label="发票号" value={detail.invoiceNo} />
          <DetailItem label="合同号" value={detail.contractNo} />
          <DetailItem label="业务" value={formatBusinessType(detail.businessType)} />
          <DetailItem label="状态" value={formatBatchStatus(detail.status)} />
          <DetailItem label="批次号" value={detail.batchReference} />
          <DetailItem label="参考号" value={detail.referenceNo} />
          <DetailItem label="提交版本" value={detail.submissionVersion} />
          <DetailItem label="草稿版本" value={detail.draftRevision} />
          <DetailItem label="创建机器" value={detail.createdOnMachine} />
          <DetailItem label="客户端配置" value={detail.clientProfileName} />
          <DetailItem label="负载文件" value={detail.payloadFileCount} />
          <DetailItem label="附件文件" value={detail.attachmentFileCount} />
          <DetailItem label="警告" value={detail.warningCount} />
          <DetailItem label="创建时间" value={formatDateTime(detail.createdAt)} />
          <DetailItem label="更新时间" value={formatDateTime(detail.updatedAt)} />
          <DetailItem label="最后派发" value={formatDateTime(detail.lastClientDispatchAt)} />
          <DetailItem label="最后回执" value={formatDateTime(detail.lastReceiptAt)} />
          <DetailItem
            label="工作目录"
            value={detail.workingDirectoryPath}
            wide
            actions={renderOpenPathAction(detail.workingDirectoryPath, "打开工作目录", setDesktopMessage)}
          />
          <DetailItem
            label="提交包路径"
            value={detail.submitPackagePath}
            wide
            actions={renderOpenPathAction(detail.submitPackagePath, "打开提交包位置", setDesktopMessage)}
          />
          <DetailItem
            label="回执包路径"
            value={detail.lastReceiptPackagePath}
            wide
            actions={renderOpenPathAction(detail.lastReceiptPackagePath, "打开回执包位置", setDesktopMessage)}
          />
          <DetailItem
            label="客户端导入目录"
            value={detail.clientDispatchPath}
            wide
            actions={renderOpenPathAction(detail.clientDispatchPath, "打开客户端导入目录", setDesktopMessage)}
          />
        </div>
      </section>

      <OperationCenterClientBridgePanel client={client} detail={detail} canOperate={canOperate} />

      <section className="form-section" aria-label="提交包记录">
        <div className="section-header">
          <h2>提交包记录</h2>
          <span className="section-count">{detail.packageRecords.length} 条</span>
        </div>
        <PackageRecordTable data={detail.packageRecords} />
      </section>

      <section className="form-section" aria-label="回执记录">
        <div className="section-header">
          <h2>回执记录</h2>
          <span className="section-count">{detail.receiptRecords.length} 条</span>
        </div>
        <ReceiptRecordTable data={detail.receiptRecords} />
      </section>

      <ReceiptPackagePanel client={client} detail={detail} canOperate={canOperate} />
    </div>
  );
}

export function OperationCenterClientBridgePanel({
  client,
  detail,
  canOperate,
}: {
  client: ExportDocManagerApiClient;
  detail: SingleWindowOperationCenterDetail;
  canOperate: boolean;
}) {
  const queryClient = useQueryClient();
  const [directoryRootPath, setDirectoryRootPath] = useState("");
  const [profileMessage, setProfileMessage] = useState<string | null>(null);
  const [dispatchMessage, setDispatchMessage] = useState<string | null>(null);
  const [desktopMessage, setDesktopMessage] = useState<string | null>(null);
  const [dispatchResult, setDispatchResult] = useState<SingleWindowClientDispatchResult | null>(null);
  const isDesktop = isDesktopBridgeAvailable();

  const profileQuery = useQuery({
    queryKey: queryKeys.singleWindowClientProfile(),
    queryFn: () => client.getSingleWindowDefaultClientProfile(),
    staleTime: 60 * 1000,
  });

  useEffect(() => {
    const profile = profileQuery.data?.profile;
    if (!profile) {
      return;
    }

    const importRoot = resolveBusinessClientRoot(profile, detail.businessType, "import");
    const receiptRoot = resolveBusinessClientRoot(profile, detail.businessType, "receipt");
    setDirectoryRootPath(importRoot || receiptRoot);
    setProfileMessage(null);
  }, [detail.businessType, profileQuery.data]);

  const saveProfileMutation = useMutation({
    mutationFn: () =>
      client.saveSingleWindowDefaultClientProfile({
        body: {
          importRootPath: directoryRootPath.trim(),
          receiptRootPath: directoryRootPath.trim(),
          businessType: detail.businessType,
        },
      }),
    onSuccess: async (response) => {
      setProfileMessage(response.message || "当前业务目录根已保存。");
      const nextRoot =
        resolveBusinessClientRoot(response.profile, detail.businessType, "import") ||
        resolveBusinessClientRoot(response.profile, detail.businessType, "receipt") ||
        directoryRootPath.trim();
      setDirectoryRootPath(nextRoot);
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
          batchId: detail.batchId,
          importRootPath: directoryRootPath.trim() || undefined,
          profileName: profileQuery.data?.profile.profileName || "",
        },
      }),
    onSuccess: async (response) => {
      setDispatchResult(response);
      setDispatchMessage("当前批次已发送到默认导入目录。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() });
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterDetail(detail.batchId) });
    },
    onError: (error) => {
      setDispatchResult(null);
      setDispatchMessage(readApiError(error));
    },
  });

  const isProfileBusy = profileQuery.isFetching || saveProfileMutation.isPending;
  const isDispatchBusy = dispatchMutation.isPending;
  const outBoxPath = buildClientBoxPath(directoryRootPath, "OutBox");
  const sentBoxPath = buildClientBoxPath(directoryRootPath, "SentBox");
  const inBoxPath = buildClientBoxPath(directoryRootPath, "InBox");
  const failBoxPath = buildClientBoxPath(directoryRootPath, "FailBox");

  function saveDirectoryRoot() {
    if (!canOperate) return;

    setProfileMessage(null);
    setDesktopMessage(null);
    if (!directoryRootPath.trim()) {
      setProfileMessage("当前业务目录根不能为空。");
      return;
    }

    saveProfileMutation.mutate();
  }

  function dispatchToDefaultImportRoot() {
    if (!canOperate) return;

    setDispatchMessage(null);
    setDesktopMessage(null);
    if (!directoryRootPath.trim()) {
      setDispatchMessage("请先保存或填写当前业务目录根。");
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
        setProfileMessage(null);
        setDispatchMessage(null);
        setDesktopMessage(null);
      }
    } catch (error) {
      setDesktopMessage(readDesktopError(error));
    }
  }

  if (!isDesktop) {
    return (
      <section className="form-section" aria-label="客户端目录操作说明">
        <div className="section-header"><h2>客户端目录操作</h2></div>
        <InlineNotice tone="info">OutBox/InBox 等本机客户端目录仅在 Tauri 桌面端管理；浏览器不会显示或接收服务器绝对路径。</InlineNotice>
      </section>
    );
  }

  return (
    <section className="form-section" aria-label="客户端目录操作">
      <div className="section-header">
        <h2>客户端目录操作</h2>
        <div className="toolbar-actions">
          <button
            className="icon-button"
            type="button"
            title="刷新客户端目录档案" aria-label="刷新客户端目录档案"
            disabled={isProfileBusy}
            onClick={() => void profileQuery.refetch()}
          >
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          <button className="command-button secondary" type="button" disabled={!canOperate || isProfileBusy} onClick={saveDirectoryRoot}>
            <Save size={17} aria-hidden="true" />
            <span>保存业务目录根</span>
          </button>
          <button className="command-button secondary" type="button" disabled={!canOperate || isDispatchBusy} onClick={dispatchToDefaultImportRoot}>
            <Send size={17} aria-hidden="true" />
            <span>发送到导入目录</span>
          </button>
        </div>
      </div>

      {!canOperate ? (
        <PermissionNotice>
          当前权限仅允许查看客户端目录和交换箱；保存与派发已禁用。
        </PermissionNotice>
      ) : null}
      {profileQuery.isError ? <InlineNotice tone="error" title="客户端配置加载失败">{readApiError(profileQuery.error)}</InlineNotice> : null}
      {profileMessage ? <InlineNotice tone={saveProfileMutation.isError ? "error" : "success"}>{profileMessage}</InlineNotice> : null}
      {dispatchMessage ? <InlineNotice tone={dispatchMutation.isError ? "error" : "success"}>{dispatchMessage}</InlineNotice> : null}
      {desktopMessage ? <InlineNotice tone="error" title="桌面目录操作失败">{desktopMessage}</InlineNotice> : null}

      <div className="field-grid">
        <PathField
          label={`${formatBusinessType(detail.businessType)}目录根`}
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
            setProfileMessage(null);
            setDispatchMessage(null);
            setDesktopMessage(null);
          }}
        />
        <DetailItem
          label="OutBox"
          value={outBoxPath}
          actions={renderOpenPathAction(outBoxPath, "打开 OutBox", setDesktopMessage)}
        />
        <DetailItem
          label="SentBox"
          value={sentBoxPath}
          actions={renderOpenPathAction(sentBoxPath, "打开 SentBox", setDesktopMessage)}
        />
        <DetailItem
          label="InBox"
          value={inBoxPath}
          actions={renderOpenPathAction(inBoxPath, "打开 InBox", setDesktopMessage)}
        />
        <DetailItem
          label="FailBox"
          value={failBoxPath}
          actions={renderOpenPathAction(failBoxPath, "打开 FailBox", setDesktopMessage)}
        />
      </div>

      {dispatchResult ? <ClientDispatchResultDetail result={dispatchResult} onOpenError={setDesktopMessage} /> : null}
    </section>
  );
}

export function ClientDispatchResultDetail({
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
      <DetailItem label="导入配置" value={result.profileName} />
      <DetailItem label="报文数" value={result.payloadFileCount} />
      <DetailItem label="附件数" value={result.attachmentFileCount} />
      <DetailItem
        label="目标目录"
        value={result.targetDirectory}
        wide
        actions={renderOpenPathAction(result.targetDirectory, "打开目标目录", onOpenError)}
      />
    </div>
  );
}
