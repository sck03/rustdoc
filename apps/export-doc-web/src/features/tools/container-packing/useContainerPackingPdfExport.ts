import { useRef, useState } from "react";
import type { ApiContainerPackingAnalysisDto, ExportDocManagerApiClient } from "../../../api/index.ts";
import { isDesktopBridgeAvailable, selectSavePdfPath } from "../../../desktop/desktopBridge.ts";
import { downloadBlob } from "../../../ui/downloadBlob.ts";
import type { ContainerPackingFormState } from "./containerPackingModel.ts";

export function useContainerPackingPdfExport(
  client: ExportDocManagerApiClient,
  projectName: string,
  container: ContainerPackingFormState,
  analysis: ApiContainerPackingAnalysisDto | null,
) {
  const resultsRootRef = useRef<HTMLDivElement | null>(null);
  const [state, setState] = useState<"idle" | "exporting">("idle");
  const [message, setMessage] = useState<{ kind: "success" | "error"; text: string } | null>(null);
  async function exportPdf() {
    if (!analysis || state === "exporting") return;
    setState("exporting");
    setMessage(null);
    try {
      const fileName = buildContainerPackingPdfFileName(projectName);
      const body = {
        projectName,
        containerType: container.containerType,
        destinationPath: "",
        container: {
          length: Number(container.length),
          width: Number(container.width),
          height: Number(container.height),
          volume: Number(container.volume),
          maxWeight: Number(container.maxWeight),
        },
        analysis,
      };
      if (isDesktopBridgeAvailable()) {
        const destinationPath = await selectSavePdfPath(fileName);
        if (!destinationPath) return;
        const result = await client.saveContainerPackingPdfToPath({ body: { ...body, destinationPath } });
        setMessage({ kind: "success", text: `PDF 已保存到 ${result.filePath}（${formatPdfSize(result.sizeBytes)}）。` });
        return;
      }

      const blob = await client.downloadContainerPackingPdf({ body });
      downloadBlob(blob, fileName);
      setMessage({ kind: "success", text: `PDF 已下载（${formatPdfSize(blob.size)}）。` });
    } catch (error) {
      const errorText = error instanceof Error ? error.message : typeof error === "string" ? error : "未知错误";
      setMessage({ kind: "error", text: `PDF 生成失败：${errorText}` });
    } finally {
      setState("idle");
    }
  }
  return { resultsRootRef, state, message, exportPdf };
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
