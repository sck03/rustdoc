import { useEffect, useMemo, useRef, useState, type PointerEvent, type WheelEvent } from "react";
import { useMutation } from "@tanstack/react-query";
import { ClipboardPaste, Copy, FileImage, Play, RotateCcw, ZoomIn, ZoomOut } from "lucide-react";
import { type ApiOcrRecognizeImageResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { isDesktopBridgeAvailable, readOcrImageFileAsDataUrl, selectOcrImageFile } from "../../desktop/desktopBridge.ts";
import { DesktopIconButton, readDesktopError, renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { PathField } from "../../ui/PathField.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";

type OcrImageSource =
  | {
      kind: "path";
      filePath: string;
    }
  | {
      kind: "content";
      imageContentBase64: string;
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

export function SmartOcrPage({ client }: { client: ExportDocManagerApiClient }) {
  const ocrPermission = useModulePermission("document.ocr");
  const desktopAvailable = isDesktopBridgeAvailable();
  const previewViewportRef = useRef<HTMLDivElement | null>(null);
  const previewDragRef = useRef<PreviewDragState | null>(null);
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
    mutationFn: () => {
      if (imageSource?.kind === "content") {
        return client.recognizeOcrImageContent({
          body: {
            imageContentBase64: imageSource.imageContentBase64,
            sourceName: imageSource.sourceName,
            sourceMimeType: imageSource.sourceMimeType,
          },
        });
      }

      const filePath = (imageSource?.kind === "path" ? imageSource.filePath : imagePath).trim();
      return client.recognizeOcrImage({
        body: {
          filePath,
        },
      });
    },
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
      if (!ocrPermission.canOperate) return;
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
  }, [ocrPermission.canOperate]);

  const isBusy = recognizeMutation.isPending;
  const canRecognize = ocrPermission.canOperate && Boolean(imageSource?.kind === "content" || imagePath.trim()) && !isBusy;
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
    resetPreview(null);

    if (!trimmed || !loadPreview || !desktopAvailable) {
      return;
    }

    try {
      const dataUrl = await readOcrImageFileAsDataUrl(trimmed);
      if (dataUrl) {
        resetPreview(dataUrl);
      }
    } catch (error) {
      showError(`图片已选择，但预览加载失败：${readDesktopError(error)}`);
    }
  }

  async function pasteImageFromClipboard() {
    if (!navigator.clipboard?.read) {
      showError("当前环境不支持读取剪贴板图片。");
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
      showError(error instanceof Error ? error.message : "读取剪贴板图片失败。");
    }
  }

  async function loadImageBlob(blob: Blob, sourceName: string) {
    if (!blob.type.startsWith("image/")) {
      showError("OCR 只支持图片内容。");
      return;
    }

    try {
      const dataUrl = await blobToDataUrl(blob);
      const imageContentBase64 = extractBase64Payload(dataUrl);
      setImagePath(sourceName);
      setImageSource({
        kind: "content",
        imageContentBase64,
        sourceName,
        sourceMimeType: blob.type,
      });
      setResult(null);
      resetPreview(dataUrl);
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
        const dataUrl = await readOcrImageFileAsDataUrl(imageSource.filePath);
        if (dataUrl) {
          resetPreview(dataUrl);
        }
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

    try {
      await navigator.clipboard.writeText(recognizedText);
      setMessage("识别文本已复制。");
      setMessageType("success");
    } catch (error) {
      showError(error instanceof Error ? error.message : "复制失败。");
    }
  }

  function resetPreview(dataUrl: string | null) {
    setImagePreviewUrl(dataUrl);
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
          disabled={isBusy || !ocrPermission.canOperate}
          onChange={(value) => {
            void usePathImage(value, false);
          }}
          actions={
            <>
              {desktopAvailable ? (
                <DesktopIconButton title="选择 OCR 图片" disabled={isBusy || !ocrPermission.canOperate} onClick={pickImage}>
                  <FileImage size={15} aria-hidden="true" />
                </DesktopIconButton>
              ) : null}
              {renderOpenPathAction(imageSource?.kind === "path" ? imageSource.filePath : "", "打开图片位置", showError)}
            </>
          }
        />
        <div className="toolbar-actions smart-ocr-action-bar">
          <button className="icon-button" type="button" title="粘贴图片" aria-label="粘贴图片" disabled={isBusy || !ocrPermission.canOperate} onClick={() => void pasteImageFromClipboard()}>
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

function blobToDataUrl(blob: Blob) {
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result ?? ""));
    reader.onerror = () => reject(reader.error ?? new Error("图片读取失败。"));
    reader.readAsDataURL(blob);
  });
}

function extractBase64Payload(dataUrl: string) {
  const separatorIndex = dataUrl.indexOf(",");
  if (separatorIndex < 0) {
    throw new Error("图片内容不是有效的 data URL。");
  }

  return dataUrl.slice(separatorIndex + 1);
}
