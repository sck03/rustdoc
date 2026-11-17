import { useMemo, useRef, useState } from "react";
import type { ExportDocManagerApiClient } from "../../../api/index.ts";
import { isDesktopBridgeAvailable, selectSavePdfPath } from "../../../desktop/desktopBridge.ts";
import { downloadBlob } from "../../../ui/downloadBlob.ts";
import { useScopedMutation } from "../../../ui/useScopedMutation.ts";
import type { ContainerPackingSnapshot } from "./containerPackingSnapshot.ts";

export function useContainerPackingPdfExport(
  client: ExportDocManagerApiClient,
  projectName: string,
  snapshot: ContainerPackingSnapshot | null,
) {
  const resultsRootRef = useRef<HTMLDivElement | null>(null);
  const owner = useMemo(() => ({ client, projectName, snapshot }), [client, projectName, snapshot]);
  const [feedback, setFeedback] = useState<{ owner: unknown; kind: "success" | "error"; text: string } | null>(null);
  const command = useScopedMutation("packing-pdf", owner, {
    mutationFn: async (_input: void, signal, { checkCurrent }) => {
      if (!snapshot?.response.analysis || !snapshot.request.container) return;
      setFeedback(null);
      const fileName = buildContainerPackingPdfFileName(projectName);
      const body = {
        projectName,
        containerType: snapshot.containerType,
        destinationPath: "",
        container: snapshot.request.container,
        analysis: snapshot.response.analysis,
      };
      if (isDesktopBridgeAvailable()) {
        const destinationPath = await selectSavePdfPath(fileName);
        checkCurrent();
        if (!destinationPath) return;
        const result = await client.saveContainerPackingPdfToPath({ body: { ...body, destinationPath } }, { signal });
        return `PDF 已保存到 ${result.filePath}（${formatPdfSize(result.sizeBytes)}）。`;
      }

      const blob = await client.downloadContainerPackingPdf({ body }, { signal });
      checkCurrent();
      downloadBlob(blob, fileName);
      return `PDF 已下载（${formatPdfSize(blob.size)}）。`;
    },
    onSuccess: text => { if (text) setFeedback({ owner, kind: "success", text }); },
    onError: error => {
      const errorText = error instanceof Error ? error.message : typeof error === "string" ? error : "未知错误";
      setFeedback({ owner, kind: "error", text: `PDF 生成失败：${errorText}` });
    },
  });
  return { resultsRootRef, state: command.isPending ? "exporting" as const : "idle" as const,
    message: feedback?.owner === owner ? feedback : null, exportPdf: () => command.mutate() };
}

function buildContainerPackingPdfFileName(projectName: string, generatedAt = new Date()) {
  const safeName = (projectName.trim() || "未命名方案").replace(/[<>:"/\\|?*\u0000-\u001f]/g, "-").replace(/\s+/g, " ").slice(0, 60);
  const date = `${generatedAt.getFullYear()}${String(generatedAt.getMonth() + 1).padStart(2, "0")}${String(generatedAt.getDate()).padStart(2, "0")}`;
  const time = `${String(generatedAt.getHours()).padStart(2, "0")}${String(generatedAt.getMinutes()).padStart(2, "0")}`;
  return `装柜方案-${safeName}-${date}-${time}.pdf`;
}

function formatPdfSize(sizeBytes: number) {
  if (sizeBytes < 1024 * 1024) return `${Math.max(1, Math.round(sizeBytes / 1024))} KB`;
  return `${(sizeBytes / 1024 / 1024).toFixed(1)} MB`;
}
