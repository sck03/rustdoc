import { useEffect, useState } from "react";
import type { ExportDocManagerApiClient, SingleWindowClientDispatchResult, SingleWindowOperationCenterRow, SingleWindowReceiptCollectionResult } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { selectSavePackagePath } from "../../desktop/desktopBridge.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { buildReceiptPackageFileName } from "./singleWindowOperationCenterModel.ts";

type Outcome = { kind: "dispatch"; response: SingleWindowClientDispatchResult } |
  { kind: "receipts"; response: SingleWindowReceiptCollectionResult; targetPath: string };

/** One lock and cancellation boundary for the selected batch's local handoff commands. */
export function useSingleWindowBatchActions(client: ExportDocManagerApiClient, row: SingleWindowOperationCenterRow, enabled: boolean) {
  const [outcome, setOutcome] = useState<Outcome | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [messageKind, setMessageKind] = useState<"success" | "error">("success");
  const scope = `${row.batchId}:${enabled}`;
  useEffect(() => { setOutcome(null); setMessage(null); }, [scope, client]);
  const command = useScopedMutation(scope, client, {
    mutationFn: async (kind: "dispatch" | "receipts", signal): Promise<Outcome> => {
      if (!enabled) throw new Error("当前工作区不允许持卡机操作。");
      if (kind === "dispatch") return { kind, response: await client.dispatchSingleWindowBatchToClient({ body: { batchId: row.batchId } }, { signal }) };
      const response = await client.collectSingleWindowClientReceipts({ body: { batchId: row.batchId } }, { signal });
      signal.throwIfAborted();
      if (!response.receiptFiles.length) throw new Error("当前档案的回执目录中尚未找到与当前批次精确匹配的回执文件。");
      const targetPath = await selectSavePackagePath(buildReceiptPackageFileName(row)) ?? "";
      signal.throwIfAborted();
      if (targetPath) await client.saveSingleWindowReceiptPackageToPath({ body: {
        businessType: row.businessType, batchReference: row.batchReference, invoiceNo: row.invoiceNo,
        receiptFiles: response.receiptFiles, packagePath: targetPath,
      } }, { signal });
      return { kind, response, targetPath };
    },
    onSuccess: result => {
      setOutcome(result);
      setMessageKind("success");
      setMessage(result.kind === "dispatch"
        ? "申报文件已送入当前档案的待导入目录；请由操作员继续确认官方客户端导入和提交。"
        : result.targetPath ? "回执包已导出，请交回办公室系统导入归档。" : "已找到回执文件，但未选择回执包保存位置。");
    },
    refreshKeys: () => [queryKeys.singleWindowOperationCenterRoot()],
    onError: error => { setOutcome(null); setMessage(readApiError(error)); setMessageKind("error"); },
  });
  return {
    message, messageKind, setMessage, isBusy: command.isPending,
    dispatch: () => command.mutate("dispatch"), collect: () => command.mutate("receipts"),
    dispatchResult: outcome?.kind === "dispatch" ? outcome.response : null,
    receiptResult: outcome?.kind === "receipts" ? outcome.response : null,
    savedReceiptPackagePath: outcome?.kind === "receipts" ? outcome.targetPath : "",
  };
}
