import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Download, PackageCheck } from "lucide-react";
import { useState } from "react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, selectSavePackagePath } from "../../desktop/desktopBridge.ts";
import { renderOpenPathAction, readDesktopError } from "../../ui/DesktopPathActions.tsx";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";

type SingleWindowHandoffPanelProps = {
  businessType: "CustomsCoo" | "AgentConsignment";
  client: ExportDocManagerApiClient;
  invoiceId: number;
  canOperate: boolean;
};

export function SingleWindowHandoffPanel({ businessType, client, invoiceId, canOperate }: SingleWindowHandoffPanelProps) {
  const queryClient = useQueryClient();
  const [message, setMessage] = useState<string | null>(null);
  const [savedPath, setSavedPath] = useState("");
  const isDesktop = isDesktopBridgeAvailable();
  const businessLabel = businessType === "CustomsCoo" ? "海关原产地证" : "报关代理委托";
  const filePrefix = businessType === "CustomsCoo" ? "COO" : "ACD";

  const exportMutation = useMutation({
    mutationFn: async () => {
      if (!isDesktop) {
        const blob = businessType === "CustomsCoo"
          ? await client.downloadCustomsCooSubmitPackage({ invoiceId })
          : await client.downloadAgentConsignmentSubmitPackage({ invoiceId });
        downloadBlob(blob, `${filePrefix}-${invoiceId}.swpkg`);
        return "";
      }

      const targetPath = await selectSavePackagePath(`${filePrefix}-${invoiceId}.swpkg`);
      if (!targetPath) {
        return null;
      }

      const response = businessType === "CustomsCoo"
        ? await client.saveCustomsCooSubmitPackageToPath({ invoiceId, body: { packagePath: targetPath } })
        : await client.saveAgentConsignmentSubmitPackageToPath({ invoiceId, body: { packagePath: targetPath } });
      return response.success ? targetPath : "";
    },
    onSuccess: async (targetPath) => {
      if (targetPath === null) {
        setMessage("已取消导出，单据内容未改变。");
        return;
      }

      setSavedPath(targetPath);
      setMessage(isDesktop
        ? "提交包已生成，请交给绑定相同公司抬头的持卡机。"
        : "提交包已交给浏览器下载，请转交对应公司的持卡机。");
      await queryClient.invalidateQueries({ queryKey: queryKeys.singleWindowOperationCenterRoot() });
    },
    onError: (error) => {
      setSavedPath("");
      setMessage(isDesktop ? readDesktopError(error) : readApiError(error));
    },
  });

  return (
    <section className="form-section single-window-handoff-card" aria-label={`${businessLabel}提交包`}>
      <div className="section-header">
        <div>
          <h2>导出持卡机提交包</h2>
          <span>制单电脑只生成 .swpkg；官方客户端操作统一在独立持卡机完成</span>
        </div>
        <button
          className="command-button"
          type="button"
          disabled={!canOperate || exportMutation.isPending}
          onClick={() => {
            setMessage(null);
            exportMutation.mutate();
          }}
        >
          {isDesktop ? <PackageCheck size={17} aria-hidden="true" /> : <Download size={17} aria-hidden="true" />}
          <span>{isDesktop ? "导出提交包" : "下载提交包"}</span>
        </button>
      </div>

      {!canOperate ? <PermissionNotice>当前权限仅允许查看单据，不能生成单一窗口提交包。</PermissionNotice> : null}
      <div className="single-window-handoff-guidance">
        <span>1. 当前页面完成字段复核</span>
        <span>2. 导出带公司绑定和完整性摘要的提交包</span>
        <span>3. 对应公司的持卡机将 XML 写入交接 OutBox，操作员再在官方客户端确认导入和提交</span>
        <span>4. 持卡机导出回执包，办公室系统再导入归档</span>
      </div>
      {message ? <InlineNotice tone={exportMutation.isError ? "error" : "success"}>{message}</InlineNotice> : null}
      {savedPath ? (
        <div className="single-window-export-path">
          <span>本机保存位置</span>
          <strong title={savedPath}>{savedPath}</strong>
          {renderOpenPathAction(savedPath, "打开提交包位置", setMessage)}
        </div>
      ) : null}
    </section>
  );
}
