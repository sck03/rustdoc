import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Files, FolderInput, Send } from "lucide-react";
import { type KeyboardEvent, useEffect, useState } from "react";
import type {
  ExportDocManagerApiClient,
  SingleWindowClientDispatchResult,
  SingleWindowOperationCenterRow,
  SingleWindowReceiptCollectionResult,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { selectSavePackagePath } from "../../desktop/desktopBridge.ts";
import { readDesktopError, renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { isDirectTableRowKeyboardEvent } from "../../ui/tableRowInteractions.ts";
import { DetailItem } from "./SingleWindowOperationCenterTables.tsx";
import {
  buildClientBoxPath,
  buildReceiptPackageFileName,
  formatBatchStatus,
  formatBusinessType,
  formatDateTime,
  invalidateSingleWindowBatchQueries,
  readDisplayText,
  resolveBusinessClientRoot,
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
        {options.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
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
    if (!isDirectTableRowKeyboardEvent(event)) return;
    if (event.key === "Enter") {
      event.preventDefault();
      onOpen(batchId);
    } else if (event.key === " ") {
      event.preventDefault();
      onSelect(batchId);
    }
  }

  return (
    <ResponsiveTableFrame label="单一窗口操作批次" mobileLayout="cards" busy={isBusy}>
      <table className="single-window-operation-table">
        <thead><tr><th>发票号</th><th>公司抬头</th><th>业务</th><th>状态</th><th>批次号</th><th>版本</th><th>回执</th><th>持卡档案</th><th>更新</th><th>操作</th></tr></thead>
        <tbody>
          {data.length === 0 ? <tr><td colSpan={10} className="empty-cell">{isBusy ? "加载中" : "暂无数据"}</td></tr> : data.map((row) => (
            <tr
              className={row.batchId === selectedBatchId ? "clickable-row selected-row" : "clickable-row"}
              key={row.batchId}
              tabIndex={0}
              aria-selected={row.batchId === selectedBatchId}
              onClick={() => onSelect(row.batchId)}
              onDoubleClick={() => onOpen(row.batchId)}
              onKeyDown={(event) => handleRowKeyDown(event, row.batchId)}
            >
              <td className="strong-cell" data-label="发票号">{readDisplayText(row.invoiceNo)}</td>
              <td data-label="公司抬头">{readDisplayText(row.companyScope)}</td>
              <td data-label="业务">{formatBusinessType(row.businessType)}</td>
              <td data-label="状态"><span className="status-pill">{formatBatchStatus(row.status)}</span></td>
              <td data-label="批次号">{readDisplayText(row.batchReference)}</td>
              <td data-label="版本">{`S${row.submissionVersion} / D${row.draftRevision}`}</td>
              <td data-label="回执" title={row.lastReceiptMessage ?? ""}>{row.receiptCount > 0 ? `${row.receiptCount} · ${readDisplayText(row.lastReceiptCode)}` : "0"}</td>
              <td data-label="持卡档案">{readDisplayText(row.clientProfileName || row.assignedCardIdentifier)}</td>
              <td data-label="更新">{formatDateTime(row.updatedAt)}</td>
              <td data-label="操作"><button className="icon-button compact-icon-button" type="button" title="打开批次详情" aria-label="打开批次详情" onClick={(event) => { event.stopPropagation(); onOpen(row.batchId); }}><Files size={15} aria-hidden="true" /></button></td>
            </tr>
          ))}
        </tbody>
      </table>
    </ResponsiveTableFrame>
  );
}

export function OperationCenterListActionsPanel({
  client,
  row,
  canOperate,
  isDesktopStation,
  onOpenDetail,
}: {
  client: ExportDocManagerApiClient;
  row: SingleWindowOperationCenterRow;
  canOperate: boolean;
  isDesktopStation: boolean;
  onOpenDetail: () => void;
}) {
  const queryClient = useQueryClient();
  const [message, setMessage] = useState<string | null>(null);
  const [messageKind, setMessageKind] = useState<"success" | "error">("success");
  const [savedReceiptPackagePath, setSavedReceiptPackagePath] = useState("");
  const [dispatchResult, setDispatchResult] = useState<SingleWindowClientDispatchResult | null>(null);
  const [receiptResult, setReceiptResult] = useState<SingleWindowReceiptCollectionResult | null>(null);

  useEffect(() => {
    setMessage(null);
    setMessageKind("success");
    setSavedReceiptPackagePath("");
    setDispatchResult(null);
    setReceiptResult(null);
  }, [row.batchId]);

  const profileQuery = useQuery({
    queryKey: queryKeys.singleWindowClientProfiles(),
    queryFn: ({ signal }) => client.getSingleWindowClientProfiles({ signal }),
    enabled: isDesktopStation,
    staleTime: 60_000,
  });

  const dispatchMutation = useMutation({
    mutationFn: () => client.dispatchSingleWindowBatchToClient({ body: { batchId: row.batchId } }),
    onSuccess: async (response) => {
      setDispatchResult(response);
      setMessage("申报文件已送入当前档案的待导入目录；这不代表官方客户端已经导入，请由操作员继续确认导入和提交。");
      setMessageKind("success");
      await invalidateSingleWindowBatchQueries(queryClient);
    },
    onError: (error) => {
      setDispatchResult(null);
      setMessage(readApiError(error));
      setMessageKind("error");
    },
  });

  const receiptMutation = useMutation({
    mutationFn: async () => {
      const collection = await client.collectSingleWindowClientReceipts({ body: { batchId: row.batchId } });
      if (collection.receiptFiles.length === 0) {
        throw new Error("当前档案的回执目录中尚未找到与当前批次精确匹配的回执文件。");
      }

      const targetPath = await selectSavePackagePath(buildReceiptPackageFileName(row));
      if (!targetPath) return { collection, targetPath: "" };
      await client.saveSingleWindowReceiptPackageToPath({
        body: {
          businessType: row.businessType,
          batchReference: row.batchReference,
          invoiceNo: row.invoiceNo,
          receiptFiles: collection.receiptFiles,
          packagePath: targetPath,
        },
      });
      return { collection, targetPath };
    },
    onSuccess: async ({ collection, targetPath }) => {
      setReceiptResult(collection);
      setSavedReceiptPackagePath(targetPath);
      setMessage(targetPath
        ? "回执包已导出，请交回办公室系统导入归档。"
        : "已找到回执文件，但未选择回执包保存位置。");
      setMessageKind("success");
      await invalidateSingleWindowBatchQueries(queryClient);
    },
    onError: (error) => {
      setReceiptResult(null);
      setSavedReceiptPackagePath("");
      setMessage(error instanceof Error ? error.message : readDesktopError(error));
      setMessageKind("error");
    },
  });

  const profile = profileQuery.data?.profiles.find((item) => item.isActive) ?? null;
  const isActiveProfileMatch = Boolean(
    profile &&
    profile.companyScope.trim().toLocaleLowerCase() === row.companyScope.trim().toLocaleLowerCase() &&
    profile.cardIdentifier === row.assignedCardIdentifier,
  );
  const clientRoot = profile && isActiveProfileMatch ? resolveBusinessClientRoot(profile, row.businessType) : "";
  const outBoxPath = buildClientBoxPath(clientRoot, "OutBox");
  const inBoxPath = buildClientBoxPath(clientRoot, "InBox");
  const isBusy = dispatchMutation.isPending || receiptMutation.isPending;
  const canDispatch = isActiveProfileMatch && row.status === "SubmitPackageImported";
  const canCollect = isActiveProfileMatch && ![
    "Preparing",
    "SubmitPackageExported",
    "SubmitPackageImported",
    "ClientDispatching",
    "ClientDispatchFailed",
  ].includes(row.status);

  return (
    <section className="form-section operation-center-list-actions" aria-label="选中批次快捷操作">
      <div className="section-header">
        <div><h2>{isDesktopStation ? "当前持卡机任务" : "当前办公室批次"}</h2><span>{readDisplayText(row.batchReference)}</span></div>
        <div className="toolbar-actions">
          <button className="command-button secondary" type="button" disabled={isBusy} onClick={onOpenDetail}><Files size={17} aria-hidden="true" /><span>批次详情</span></button>
          {isDesktopStation ? <button className="command-button" type="button" disabled={!canOperate || isBusy || !canDispatch} onClick={() => dispatchMutation.mutate()}><Send size={17} aria-hidden="true" /><span>送入官方客户端待办目录</span></button> : null}
          {isDesktopStation ? <button className="command-button secondary" type="button" disabled={!canOperate || isBusy || !canCollect} onClick={() => receiptMutation.mutate()}><FolderInput size={17} aria-hidden="true" /><span>收集并导出回执</span></button> : null}
        </div>
      </div>

      {!canOperate ? <PermissionNotice>当前权限仅允许查看批次详情。</PermissionNotice> : null}
      {!isDesktopStation ? <InlineNotice tone="info">办公室端不显示持卡机本地目录。请在上方导入对应持卡机返回的回执包。</InlineNotice> : null}
      {profileQuery.isError ? <InlineNotice tone="error">{readApiError(profileQuery.error)}</InlineNotice> : null}
      {isDesktopStation && profile && !isActiveProfileMatch ? (
        <InlineNotice tone="warning" title="请先切换操作卡">
          当前批次绑定“{row.clientProfileName || row.companyScope} / {row.assignedCardIdentifier}”，当前启用的是“{profile.profileName} / {profile.cardIdentifier}”。
        </InlineNotice>
      ) : null}
      {isDesktopStation && !profile ? <InlineNotice tone="warning">请先创建并启用公司与操作卡档案。</InlineNotice> : null}
      {message ? <InlineNotice tone={messageKind}>{message}</InlineNotice> : null}

      <div className="operation-center-list-action-grid">
        <DetailItem label="发票号" value={row.invoiceNo} />
        <DetailItem label="公司抬头" value={row.companyScope} />
        <DetailItem label="业务" value={formatBusinessType(row.businessType)} />
        <DetailItem label="状态" value={formatBatchStatus(row.status)} />
        {isDesktopStation ? <DetailItem label="操作卡" value={profile?.cardIdentifier || row.assignedCardIdentifier} /> : <DetailItem label="持卡档案" value={row.clientProfileName || row.assignedCardIdentifier} />}
        {isDesktopStation ? <DetailItem label="待导入目录" value={outBoxPath} actions={renderOpenPathAction(outBoxPath, "打开待导入目录", setMessage)} /> : null}
        {isDesktopStation ? <DetailItem label="回执目录" value={inBoxPath} actions={renderOpenPathAction(inBoxPath, "打开回执目录", setMessage)} /> : null}
        {dispatchResult ? <DetailItem label="已写入文件" value={dispatchResult.payloadFileCount} /> : null}
        {receiptResult ? <DetailItem label="已收集回执" value={receiptResult.receiptFiles.length} /> : null}
        {savedReceiptPackagePath ? <DetailItem label="回执包" value={savedReceiptPackagePath} wide actions={renderOpenPathAction(savedReceiptPackagePath, "打开回执包位置", setMessage)} /> : null}
      </div>
    </section>
  );
}
