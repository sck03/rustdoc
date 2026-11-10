import { useEffect, useState } from "react";
import type { ApiSingleWindowImportedPackageResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { selectSingleWindowPackageFile } from "../../desktop/desktopBridge.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { formatBatchStatus } from "./singleWindowOperationCenterModel.ts";

export function useSingleWindowPackageImport(client: ExportDocManagerApiClient, enabled: boolean, profileKey?: string) {
  const [packagePath, setPackagePath] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [result, setResult] = useState<ApiSingleWindowImportedPackageResponse | null>(null);
  const [isError, setError] = useState(false);
  useEffect(() => { setFile(null); setPackagePath(""); setResult(null); setMessage(null); }, [client, profileKey]);
  type Result = { path: string | null } | { response: ApiSingleWindowImportedPackageResponse };
  const command = useScopedMutation(`${enabled}:${profileKey ?? "office"}`, client, {
    mutationFn: async (kind: "choose" | "submit" | "receipt", signal): Promise<Result> => {
      if (!enabled) throw new Error("当前工作区不允许导入交接包。");
      if (kind === "choose") return { path: await selectSingleWindowPackageFile() };
      if (kind === "submit") return { response: await client.importSingleWindowSubmitPackage({ body: { packagePath: packagePath.trim(), keepWorkingDirectory: false } }, { signal }) };
      if (!file) throw new Error("请选择回执包文件。");
      return { response: await client.uploadSingleWindowReceiptPackage({ fileName: file.name, keepWorkingDirectory: false, body: file }, { signal }) };
    },
    onSuccess: response => {
      setError(false);
      if ("path" in response) { if (response.path) setPackagePath(response.path); return; }
      setResult(response.response); setFile(null);
      setMessage(profileKey ? response.response.message || "提交包已绑定到本持卡机。"
        : `回执包已归档，新增 ${response.response.persistedReceiptCount} 条回执，批次状态为“${formatBatchStatus(response.response.trackingStatus)}”。`);
    },
    refreshKeys: response => "response" in response ? [queryKeys.singleWindowOperationCenterRoot()] : [],
    onError: error => { setResult(null); setError(true); setMessage(readApiError(error)); },
  });
  return { packagePath, setPackagePath, file, setFile, message, setMessage, result, isError,
    isBusy: command.isPending, choosePackage: () => command.mutate("choose"),
    submit: () => command.mutate("submit"), receipt: () => command.mutate("receipt") };
}
