import { useEffect, useMemo, useRef, useState, type PointerEvent, type WheelEvent } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { ClipboardPaste, Copy, FileImage, Play, RotateCcw, ZoomIn, ZoomOut } from "lucide-react";
import { type ApiOcrRecognizeImageResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { isDesktopBridgeAvailable, selectOcrImageFile } from "../../desktop/desktopBridge.ts";
import { DesktopIconButton, readDesktopError, renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { PathField } from "../../ui/PathField.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { getClipboardPasteInstruction, writeClipboardText } from "../../ui/clipboard.ts";
import { useAbortableOperation } from "../../ui/useAbortableOperation.ts";

type OcrImageSource =
  | {
      kind: "path";
      filePath: string;
    }
  | {
      kind: "content";
      blob: Blob;
      sourceName: string;
      sourceMimeType: string;
    };

type ImageSize = {
  width: number;
  height: number;
};

type PreviewDragState = {
  pointerId: number;
  clientX: number;
  clientY: number;
  scrollLeft: number;
  scrollTop: number;
};

const MinZoom = 0.1;
const MaxZoom = 10;
const MaxOcrImageBytes = 25 * 1024 * 1024;

export function SmartOcrPage({ client }: { client: ExportDocManagerApiClient }) {
  const ocrPermission = useModulePermission("document.ocr");
  const desktopAvailable = isDesktopBridgeAvailable();
  const runAbortableOperation = useAbortableOperation();
  const healthQuery = useQuery({
    queryKey: queryKeys.health(),
    queryFn: ({ signal }) => client.getHealth({ signal }),
  });
  const ocrRuntime = healthQuery.data?.runtimeDependencies.find((item) => item.key === "ocr-runtime") ?? null;
  const ocrRuntimeReady = healthQuery.isSuccess && ocrRuntime?.status === "ready" && ocrRuntime.ready;
  const ocrRuntimeUnavailable = healthQuery.isSuccess && !ocrRuntimeReady;
  const canUseOcr = ocrPermission.canOperate && ocrRuntimeReady;
  const previewViewportRef = useRef<HTMLDivElement | null>(null);
  const previewDragRef = useRef<PreviewDragState | null>(null);
  const previewObjectUrlRef = useRef<string | null>(null);
  const [imagePath, setImagePath] = useState("");
  const [imageSource, setImageSource] = useState<OcrImageSource | null>(null);
  const [imagePreviewUrl, setImagePreviewUrl] = useState<string | null>(null);
  const [previewSize, setPreviewSize] = useState<ImageSize | null>(null);
  const [zoom, setZoom] = useState(1);
  const [isDraggingPreview, setIsDraggingPreview] = useState(false);
  const [result, setResult] = useState<ApiOcrRecognizeImageResponse | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [messageType, setMessageType] = useState<"success" | "error">("success");

  const recognizeMutation = useMutation({
    mutationFn: () => runAbortableOperation((signal) => {
      if (imageSource?.kind === "content") {
        return client.uploadOcrImage({
          body: imageSource.blob,
          sourceName: imageSource.sourceName,
          sourceMimeType: imageSource.sourceMimeType,
        }, { signal });
      }

      const filePath = (imageSource?.kind === "path" ? imageSource.filePath : imagePath).trim();
      return client.recognizeOcrImage({
        body: {
          filePath,
        },
      }, { signal });
    }),
    onSuccess: (response) => {
      setResult(response);
      setMessage("OCR 识别完成。");
      setMessageType("success");
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setMessageType("error");
    },
  });

  const recognizedText = useMemo(() => {
    if (!result) {
      return "";
    }

    const fullText = result.fullText?.trim();
    if (fullText) {
      return fullText;
    }

    return [...(result.lines ?? [])]
      .sort((left, right) => left.y - right.y || left.x - right.x)
      .map((line) => line.text)
      .filter(Boolean)
      .join("\n");
  }, [result]);

  useEffect(() => {
    function handlePaste(event: ClipboardEvent) {
      if (!canUseOcr) return;
      const items = Array.from(event.clipboardData?.items ?? []);
      const imageItem = items.find((item) => item.type.startsWith("image/"));
      const file = imageItem?.getAsFile();
      if (!file) {
        return;
      }

      event.preventDefault();
      void loadImageBlob(file, "剪贴板图片（内存）");
    }

    window.addEventListener("paste", handlePaste);
    return () => window.removeEventListener("paste", handlePaste);
  }, [canUseOcr]);

  useEffect(() => () => {
    if (previewObjectUrlRef.current) {
      window.URL.revokeObjectURL(previewObjectUrlRef.current);
    }
  }, []);

  const isBusy = recognizeMutation.isPending;
  const canRecognize = canUseOcr && Boolean(imageSource?.kind === "content" || imagePath.trim()) && !isBusy;
  const canCopy = Boolean(recognizedText.trim());
  const lines = result?.lines ?? [];
  const pathSource = imageSource?.kind === "path" ? imageSource.filePath : "";
  const sourceLabel =
    imageSource?.kind === "content"
      ? imageSource.sourceName
      : result?.sourcePath || pathSource || imagePath || "未选择图片";
  const zoomLabel = `${Math.round(zoom * 100)}%`;

  async function pickImage() {
    try {
      const selected = await selectOcrImageFile();
      if (selected) {
        await usePathImage(selected, true);
      }
    } catch (error) {
      showError(readDesktopError(error));
    }
  }

  async function usePathImage(path: string, loadPreview: boolean) {
    const trimmed = path.trim();
    setImagePath(trimmed);
    setImageSource(trimmed ? { kind: "path", filePath: trimmed } : null);
    setResult(null);
    setMessage(null);
    replacePreview(null);

    if (!trimmed || !loadPreview || !desktopAvailable) {
      return;
    }

    try {
      replacePreview(await client.previewOcrImage({ filePath: trimmed }));
    } catch (error) {
      showError(`图片已选择，但预览加载失败：${readDesktopError(error)}`);
    }
  }

  async function pasteImageFromClipboard() {
    if (!window.isSecureContext || !navigator.clipboard?.read) {
      showError(getClipboardPasteInstruction("页面"));
      return;
    }

    try {
      const items = await navigator.clipboard.read();
      for (const item of items) {
        const imageType = item.types.find((type) => type.startsWith("image/"));
        if (!imageType) {
          continue;
        }

        const blob = await item.getType(imageType);
        await loadImageBlob(blob, "剪贴板图片（内存）");
        return;
      }

      showError("剪贴板中没有图片。");
    } catch (error) {
      showError(`${error instanceof Error ? error.message : "读取剪贴板图片失败。"} ${getClipboardPasteInstruction("页面")}`);
    }
  }

  async function loadImageBlob(blob: Blob, sourceName: string) {
    if (!blob.type.startsWith("image/")) {
      showError("OCR 只支持图片内容。");
      return;
    }
    if (blob.size > MaxOcrImageBytes) {
      showError(`图片不能超过 ${Math.floor(MaxOcrImageBytes / 1024 / 1024)} MB。`);
      return;
    }

    try {
      setImagePath(sourceName);
      setImageSource({
        kind: "content",
        blob,
        sourceName,
        sourceMimeType: blob.type,
      });
      setResult(null);
      replacePreview(blob);
      setMessage("图片已载入。");
      setMessageType("success");
    } catch (error) {
      showError(error instanceof Error ? error.message : "图片载入失败。");
    }
  }

  async function recognizeCurrentImage() {
    if (!canRecognize) {
      return;
    }

    if (imageSource?.kind === "path" && desktopAvailable && !imagePreviewUrl) {
      try {
        replacePreview(await client.previewOcrImage({ filePath: imageSource.filePath }));
      } catch {
        // Preview is helpful but not required for path-based OCR recognition.
      }
    }

    recognizeMutation.mutate();
  }

  async function copyText() {
    if (!canCopy) {
      return;
    }

    if (await writeClipboardText(recognizedText)) {
      setMessage("识别文本已复制。");
      setMessageType("success");
    } else {
      showError("复制失败，请手动选中文本复制。");
    }
  }

  function replacePreview(blob: Blob | null) {
    if (previewObjectUrlRef.current) {
      window.URL.revokeObjectURL(previewObjectUrlRef.current);
    }
    const objectUrl = blob ? window.URL.createObjectURL(blob) : null;
    previewObjectUrlRef.current = objectUrl;
    setImagePreviewUrl(objectUrl);
    setPreviewSize(null);
    setZoom(1);
    window.requestAnimationFrame(() => {
      const viewport = previewViewportRef.current;
      if (viewport) {
        viewport.scrollLeft = 0;
        viewport.scrollTop = 0;
      }
    });
  }

  function adjustZoom(multiplier: number) {
    setZoom((current) => clampZoom(current * multiplier));
  }

  function resetZoom() {
    setZoom(1);
    window.requestAnimationFrame(() => {
      const viewport = previewViewportRef.current;
      if (viewport) {
        viewport.scrollLeft = 0;
        viewport.scrollTop = 0;
      }
    });
  }

  function handlePreviewWheel(event: WheelEvent<HTMLDivElement>) {
    if (!event.ctrlKey || !imagePreviewUrl) {
      return;
    }

    event.preventDefault();
    adjustZoom(event.deltaY < 0 ? 1.1 : 0.9);
  }

  function beginPreviewDrag(event: PointerEvent<HTMLDivElement>) {
    const viewport = previewViewportRef.current;
    if (!viewport || !imagePreviewUrl || event.button !== 0) {
      return;
    }

    event.preventDefault();
    previewDragRef.current = {
      pointerId: event.pointerId,
      clientX: event.clientX,
      clientY: event.clientY,
      scrollLeft: viewport.scrollLeft,
      scrollTop: viewport.scrollTop,
    };
    setIsDraggingPreview(true);
    event.currentTarget.setPointerCapture(event.pointerId);
  }

  function movePreviewDrag(event: PointerEvent<HTMLDivElement>) {
    const viewport = previewViewportRef.current;
    const drag = previewDragRef.current;
    if (!viewport || !drag || drag.pointerId !== event.pointerId) {
      return;
    }

    viewport.scrollLeft = drag.scrollLeft - (event.clientX - drag.clientX);
    viewport.scrollTop = drag.scrollTop - (event.clientY - drag.clientY);
  }

  function endPreviewDrag(event: PointerEvent<HTMLDivElement>) {
    const drag = previewDragRef.current;
    if (drag?.pointerId === event.pointerId) {
      event.currentTarget.releasePointerCapture(event.pointerId);
      previewDragRef.current = null;
      setIsDraggingPreview(false);
    }
  }

  function showError(value: string) {
    setMessage(value);
    setMessageType("error");
  }

  return (
    <section className="work-surface smart-ocr-surface" aria-label="智能 OCR">
      <div className="toolbar smart-ocr-toolbar">
        <PathField
          label="图片路径"
          value={imagePath}
          disabled={isBusy || !canUseOcr}
          onChange={(value) => {
            void usePathImage(value, false);
          }}
          actions={
            <>
              {desktopAvailable ? (
                <DesktopIconButton title="选择 OCR 图片" disabled={isBusy || !canUseOcr} onClick={pickImage}>
                  <FileImage size={15} aria-hidden="true" />
                </DesktopIconButton>
              ) : null}
              {renderOpenPathAction(imageSource?.kind === "path" ? imageSource.filePath : "", "打开图片位置", showError)}
            </>
          }
        />
        <div className="toolbar-actions smart-ocr-action-bar">
          <button className="icon-button" type="button" title="粘贴图片" aria-label="粘贴图片" disabled={isBusy || !canUseOcr} onClick={() => void pasteImageFromClipboard()}>
            <ClipboardPaste size={18} aria-hidden="true" />
          </button>
          <button className="icon-button" type="button" title="复制文本" aria-label="复制文本" disabled={!canCopy} onClick={() => void copyText()}>
            <Copy size={18} aria-hidden="true" />
          </button>
          <button className="icon-button solid" type="button" title="开始识别" aria-label="开始识别" disabled={!canRecognize} onClick={() => void recognizeCurrentImage()}>
            <Play size={18} aria-hidden="true" />
          </button>
        </div>
      </div>

      {!ocrPermission.canOperate ? <PermissionNotice>当前模板仅允许进入 OCR 模块，图片载入和识别操作已禁用。</PermissionNotice> : null}
      {ocrPermission.canOperate && healthQuery.isPending ? (
        <InlineNotice tone="info" title="正在检查智能识别组件">确认本机 OCR 运行组件状态后才能开始识别。</InlineNotice>
      ) : null}
      {ocrPermission.canOperate && healthQuery.isError ? (
        <InlineNotice tone="error" title="无法确认智能识别组件状态">暂不能安全启用 OCR，请恢复业务服务后重试。</InlineNotice>
      ) : null}
      {ocrPermission.canOperate && ocrRuntimeUnavailable ? (
        <InlineNotice tone="warning" title="智能识别暂不可用">
          {readOcrRuntimeUnavailableMessage(ocrRuntime?.status)}
        </InlineNotice>
      ) : null}
      {message ? <InlineNotice tone={messageType === "error" ? "error" : "success"}>{message}</InlineNotice> : null}

      <div className="smart-ocr-layout">
        <section className="form-section smart-ocr-preview-panel" aria-label="图片预览">
          <div className="section-header">
            <div>
              <h2>图片预览</h2>
              <span>{sourceLabel}</span>
            </div>
            <div className="smart-ocr-preview-tools">
              <button className="icon-button compact-icon-button" type="button" title="缩小" aria-label="缩小" disabled={!imagePreviewUrl} onClick={() => adjustZoom(0.8)}>
                <ZoomOut size={16} aria-hidden="true" />
              </button>
              <span className="smart-ocr-zoom-readout">{zoomLabel}</span>
              <button className="icon-button compact-icon-button" type="button" title="放大" aria-label="放大" disabled={!imagePreviewUrl} onClick={() => adjustZoom(1.25)}>
                <ZoomIn size={16} aria-hidden="true" />
              </button>
              <button className="icon-button compact-icon-button" type="button" title="重置缩放" aria-label="重置缩放" disabled={!imagePreviewUrl} onClick={resetZoom}>
                <RotateCcw size={16} aria-hidden="true" />
              </button>
            </div>
          </div>
          <div
            ref={previewViewportRef}
            className={isDraggingPreview ? "smart-ocr-preview-viewport smart-ocr-preview-viewport-dragging" : "smart-ocr-preview-viewport"}
            onWheel={handlePreviewWheel}
            onPointerDown={beginPreviewDrag}
            onPointerMove={movePreviewDrag}
            onPointerUp={endPreviewDrag}
            onPointerCancel={endPreviewDrag}
            aria-label="OCR 图片预览画布"
          >
            {imagePreviewUrl ? (
              <div
                className="smart-ocr-preview-canvas"
                style={
                  previewSize
                    ? {
                        width: `${Math.max(1, previewSize.width * zoom)}px`,
                        height: `${Math.max(1, previewSize.height * zoom)}px`,
                      }
                    : undefined
                }
              >
                <img
                  src={imagePreviewUrl}
                  alt=""
                  draggable={false}
                  onLoad={(event) => {
                    setPreviewSize({
                      width: event.currentTarget.naturalWidth,
                      height: event.currentTarget.naturalHeight,
                    });
                  }}
                />
                {previewSize ? (
                  <div className="smart-ocr-overlay" aria-hidden="true">
                    {lines.map((line, index) => (
                      <span
                        className="smart-ocr-line-box"
                        key={`${line.x}-${line.y}-${index}`}
                        title={line.text || undefined}
                        style={{
                          left: `${line.x * zoom}px`,
                          top: `${line.y * zoom}px`,
                          width: `${Math.max(1, line.width * zoom)}px`,
                          height: `${Math.max(1, line.height * zoom)}px`,
                        }}
                      />
                    ))}
                  </div>
                ) : null}
              </div>
            ) : (
              <div className="smart-ocr-preview-empty">未载入图片预览</div>
            )}
          </div>
        </section>

        <div className="smart-ocr-side-panel">
          <section className="form-section smart-ocr-result-panel" aria-label="识别结果">
            <div className="section-header">
              <div>
                <h2>识别结果</h2>
                <span>{result ? `${result.lines?.length ?? 0} 行` : "等待识别"}</span>
              </div>
            </div>
            <textarea value={recognizedText || (isBusy ? "正在识别中，请稍候..." : "")} readOnly />
          </section>
        </div>
      </div>
    </section>
  );
}

function clampZoom(value: number) {
  return Math.min(MaxZoom, Math.max(MinZoom, Number(value.toFixed(3))));
}

function readOcrRuntimeUnavailableMessage(status?: string) {
  switch (status) {
    case "disabled":
      return "智能识别功能已在当前安装中关闭。如需使用，请联系系统管理员启用。";
    case "incomplete":
      return "智能识别组件安装不完整，请联系系统管理员修复后再试。";
    case "unsupported":
      return "当前系统平台暂不支持智能识别，其它业务功能不受影响。";
    default:
      return "当前安装未包含智能识别组件，其它业务功能不受影响。";
  }
}
