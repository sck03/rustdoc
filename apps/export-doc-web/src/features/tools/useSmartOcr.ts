import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { type ApiOcrRecognizeImageResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, selectOcrImageFile } from "../../desktop/desktopBridge.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { getClipboardPasteInstruction, writeClipboardText } from "../../ui/clipboard.ts";
import { isAbortError, useAbortableOperation, useAbortableOperationController } from "../../ui/useAbortableOperation.ts";
import { useBlobUrl } from "../../ui/useBlobUrl.ts";

type OcrImageSource = { kind: "path"; filePath: string } | { kind: "content"; blob: Blob; sourceName: string; sourceMimeType: string };
export const OCR_IMAGE_ACCEPT = ".png,.jpg,.jpeg,.bmp,.webp,.tif,.tiff";
const MIME_TYPES: Record<string, string> = { png: "image/png", jpg: "image/jpeg", jpeg: "image/jpeg", bmp: "image/bmp", webp: "image/webp", tif: "image/tiff", tiff: "image/tiff" };

export function useSmartOcr(client: ExportDocManagerApiClient, permitted: boolean) {
  const desktopAvailable = isDesktopBridgeAvailable();
  const healthQuery = useQuery({ queryKey: queryKeys.health(), queryFn: ({ signal }) => client.getHealth({ signal }) });
  const ocrRuntime = healthQuery.data?.runtimeDependencies.find(item => item.key === "ocr-runtime") ?? null;
  const canUseOcr = permitted && healthQuery.isSuccess && ocrRuntime?.status === "ready" && ocrRuntime.ready;
  const [imageSource, setImageSource] = useState<OcrImageSource | null>(null);
  const sourceRef = useRef<OcrImageSource | null>(null);
  const inputSequence = useRef(0);
  const activeRecognition = useRef<OcrImageSource | null>(null);
  const [preview, setPreview] = useState<Blob | null>(null);
  const imagePreviewUrl = useBlobUrl(preview);
  const [result, setResult] = useState<ApiOcrRecognizeImageResponse | null>(null);
  const [isBusy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [messageType, setMessageType] = useState<"success" | "error" | "info">("success");
  const { run: runRecognition, cancel: cancelRecognition } = useAbortableOperationController(undefined, { cancelPrevious: true });
  const { run: runPreview, cancel: cancelPreview } = useAbortableOperationController(undefined, { cancelPrevious: true });
  const runInput = useAbortableOperation(undefined, { cancelPrevious: true });
  const showError = useCallback((value: string) => { setMessage(value); setMessageType("error"); }, []);
  useEffect(() => () => { sourceRef.current = null; inputSequence.current++; }, []);

  const chooseSource = useCallback((source: OcrImageSource | null) => {
    cancelRecognition(); cancelPreview();
    inputSequence.current++;
    sourceRef.current = source;
    activeRecognition.current = null;
    setImageSource(source);
    setPreview(source?.kind === "content" ? source.blob : null);
    setResult(null); setMessage(null); setBusy(false);
  }, [cancelRecognition, cancelPreview]);

  const loadPreview = useCallback(async (source: OcrImageSource) => {
    if (source.kind !== "path") return;
    try {
      const blob = await runPreview(signal => client.previewOcrImage({ filePath: source.filePath }, { signal }));
      if (sourceRef.current === source) setPreview(blob);
    } catch (error) {
      if (!isAbortError(error) && sourceRef.current === source) showError(`图片已选择，但预览加载失败：${readApiError(error)}`);
    }
  }, [client, runPreview, showError]);

  const usePathImage = useCallback((filePath: string, withPreview: boolean) => {
    if (!desktopAvailable) return;
    const source: OcrImageSource | null = filePath.trim() ? { kind: "path", filePath } : null;
    chooseSource(source);
    if (source && withPreview) void loadPreview(source);
  }, [chooseSource, desktopAvailable, loadPreview]);

  const loadImageBlob = useCallback((blob: Blob, sourceName: string) => {
    const mime = blob.type || MIME_TYPES[sourceName.split(".").at(-1)?.toLowerCase() ?? ""];
    if (!Object.values(MIME_TYPES).includes(mime)) { showError("请选择 PNG、JPEG、BMP、WebP 或 TIFF 图片。"); return; }
    if (!blob.size || blob.size > 25 * 1024 * 1024) { showError("图片必须非空且不能超过 25 MiB。"); return; }
    chooseSource({ kind: "content", blob, sourceName, sourceMimeType: mime });
    setMessage("图片已载入。"); setMessageType("success");
  }, [chooseSource, showError]);

  useEffect(() => {
    const paste = (event: ClipboardEvent) => {
      if (!canUseOcr) return;
      const file = Array.from(event.clipboardData?.items ?? []).find(item => item.type.startsWith("image/"))?.getAsFile();
      if (!file) return;
      event.preventDefault(); loadImageBlob(file, "剪贴板图片");
    };
    window.addEventListener("paste", paste);
    return () => window.removeEventListener("paste", paste);
  }, [canUseOcr, loadImageBlob]);

  async function pickImage() {
    const sequence = ++inputSequence.current;
    try {
      const selected = await runInput(() => selectOcrImageFile());
      if (selected && sequence === inputSequence.current) usePathImage(selected, true);
    } catch (error) { if (!isAbortError(error) && sequence === inputSequence.current) showError(readApiError(error)); }
  }

  function manualPaste(reason: string) {
    setMessage(`${reason} ${getClipboardPasteInstruction("图片预览区")}`); setMessageType("info");
    return "manual" as const;
  }
  async function pasteImageFromClipboard(): Promise<"manual" | undefined> {
    if (!canUseOcr) return;
    if (!window.isSecureContext) return manualPaste("当前 HTTP 连接不能一键读取剪贴板；使用 HTTPS 可启用此功能。");
    if (!navigator.clipboard?.read) return manualPaste("当前浏览器不支持一键读取剪贴板图片。");
    const sequence = ++inputSequence.current;
    try {
      const blob = await runInput(async signal => {
        const items = await navigator.clipboard.read();
        signal.throwIfAborted();
        for (const item of items) {
          const type = item.types.find(type => type.startsWith("image/"));
          if (type) return item.getType(type);
        }
        return null;
      });
      if (sequence === inputSequence.current) {
        if (blob) loadImageBlob(blob, "剪贴板图片");
        else showError("剪贴板中没有图片，请先截图或复制图片。");
      }
    } catch (error) { if (!isAbortError(error) && sequence === inputSequence.current) return manualPaste("浏览器未允许直接读取剪贴板图片。"); }
  }

  async function recognizeCurrentImage() {
    const source = sourceRef.current;
    if (!canUseOcr || !source || activeRecognition.current) return;
    activeRecognition.current = source; setBusy(true); setMessage(null);
    if (source.kind === "path" && !preview) void loadPreview(source);
    try {
      const response = await runRecognition(signal => source.kind === "content"
        ? client.uploadOcrImage({ body: source.blob, sourceName: source.sourceName, sourceMimeType: source.sourceMimeType }, { signal })
        : client.recognizeOcrImage({ body: { filePath: source.filePath.trim() } }, { signal }));
      if (sourceRef.current !== source) return;
      setResult(response); setMessage("OCR 识别完成。"); setMessageType("success");
    } catch (error) { if (!isAbortError(error) && sourceRef.current === source) showError(readApiError(error)); }
    finally {
      if (activeRecognition.current === source) { activeRecognition.current = null; setBusy(false); }
    }
  }

  const recognizedText = useMemo(() => result?.fullText?.trim() || (result?.lines ?? []).map(line => line.text).filter(Boolean).join("\n"), [result]);
  async function copyText() {
    const source = sourceRef.current;
    if (!recognizedText.trim()) return;
    const copied = await writeClipboardText(recognizedText);
    if (sourceRef.current !== source) return;
    if (copied) { setMessage("识别文本已复制。"); setMessageType("success"); }
    else showError("复制失败，请手动选中文本复制。");
  }

  const imagePath = imageSource?.kind === "path" ? imageSource.filePath : imageSource?.sourceName ?? "";
  return { desktopAvailable, healthQuery, ocrRuntime, canUseOcr, imageSource, imagePath, imagePreviewUrl, result,
    isBusy, message, messageType, recognizedText, canRecognize: Boolean(canUseOcr && imageSource && !isBusy),
    canCopy: Boolean(recognizedText.trim()), sourceLabel: imagePath || "未选择图片",
    pickImage, usePathImage, loadImageBlob, pasteImageFromClipboard, recognizeCurrentImage, copyText, showError };
}
