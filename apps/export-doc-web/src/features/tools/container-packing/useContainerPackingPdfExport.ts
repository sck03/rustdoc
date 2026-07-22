import { useRef, useState } from "react";

export function useContainerPackingPdfExport(projectName: string, containerType: string) {
  const pdfRootRef = useRef<HTMLDivElement | null>(null);
  const [state, setState] = useState<"idle" | "exporting">("idle");
  const [message, setMessage] = useState<{ kind: "success" | "error"; text: string } | null>(null);
  async function exportPdf() {
    if (!pdfRootRef.current || state === "exporting") return;
    setState("exporting");
    setMessage(null);
    try {
      const { exportContainerPackingPdf } = await import("./containerPackingPdfExport.ts");
      const result = await exportContainerPackingPdf({ root: pdfRootRef.current, projectName, containerType });
      if (result.status === "cancelled") return;
      const sizeText = formatPdfSize(result.sizeBytes ?? 0);
      if (result.status === "save-failed") {
        setMessage({ kind: "error", text: `PDF 已生成，但没有保存成功：${result.error}。请重新点击“导出 PDF”并选择保存位置。` });
        return;
      }
      setMessage({ kind: "success", text: result.mode === "desktop" ? `PDF 已保存到 ${result.path}（${result.pageCount} 页，${sizeText}）` : `PDF 已下载（${result.pageCount} 页，${sizeText}）。` });
    } catch (error) {
      const errorText = error instanceof Error ? error.message : typeof error === "string" ? error : "未知错误";
      setMessage({ kind: "error", text: `PDF 生成失败：${errorText}` });
    } finally {
      setState("idle");
    }
  }
  return { pdfRootRef, state, message, exportPdf };
}

function formatPdfSize(sizeBytes: number) {
  if (sizeBytes < 1024 * 1024) return `${Math.max(1, Math.round(sizeBytes / 1024))} KB`;
  return `${(sizeBytes / 1024 / 1024).toFixed(1)} MB`;
}
