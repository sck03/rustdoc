import { useEffect, useRef, useState, type PointerEvent, type WheelEvent } from "react";
import { ClipboardPaste, Copy, FileImage, Play, RotateCcw, ZoomIn, ZoomOut } from "lucide-react";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { DesktopIconButton, renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { PathField } from "../../ui/PathField.tsx";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { OCR_IMAGE_ACCEPT, useSmartOcr } from "./useSmartOcr.ts";

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
  const { desktopAvailable, healthQuery, ocrRuntime, canUseOcr, imageSource, imagePath, imagePreviewUrl, result,
    isBusy, message, messageType, recognizedText, canRecognize, canCopy, sourceLabel,
    pickImage, usePathImage, loadImageBlob, pasteImageFromClipboard, recognizeCurrentImage, copyText, showError } = useSmartOcr(client, ocrPermission.canOperate);
  const ocrRuntimeUnavailable = healthQuery.isSuccess && !canUseOcr;
  const fileInput = useRef<HTMLInputElement | null>(null);
  const previewViewportRef = useRef<HTMLDivElement | null>(null);
  const previewDragRef = useRef<PreviewDragState | null>(null);
  const [previewSize, setPreviewSize] = useState<ImageSize | null>(null);
  const [zoom, setZoom] = useState(1);
  const [isDraggingPreview, setIsDraggingPreview] = useState(false);
  const zoomLabel = `${Math.round(zoom * 100)}%`;
  const lines = result?.lines ?? [];
  useEffect(() => {
    setPreviewSize(null); setZoom(1); setIsDraggingPreview(false); previewDragRef.current = null;
    const frame = requestAnimationFrame(() => {
      if (previewViewportRef.current) { previewViewportRef.current.scrollLeft = 0; previewViewportRef.current.scrollTop = 0; }
    });
    return () => cancelAnimationFrame(frame);
  }, [imagePreviewUrl]);

  async function pasteImage() {
    if (await pasteImageFromClipboard() === "manual") previewViewportRef.current?.focus({ preventScroll: true });
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
    if (!viewport || !imagePreviewUrl || event.button !== 0 || previewDragRef.current) {
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
      if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
      previewDragRef.current = null;
      setIsDraggingPreview(false);
    }
  }

  return (
    <section className="work-surface smart-ocr-surface" aria-label="智能 OCR">
      <div className="toolbar smart-ocr-toolbar">
        <PathField
          label={desktopAvailable ? "图片路径" : "图片文件"}
          value={imagePath}
          readOnly={!desktopAvailable}
          disabled={isBusy || !canUseOcr}
          onChange={(value) => {
            void usePathImage(value, false);
          }}
          actions={
            <>
              <input ref={fileInput} type="file" hidden accept={OCR_IMAGE_ACCEPT} aria-label="上传 OCR 图片" onChange={event => {
                const file = event.currentTarget.files?.[0];
                if (file) loadImageBlob(file, file.name);
                event.currentTarget.value = "";
              }} />
                <DesktopIconButton title="选择 OCR 图片" disabled={isBusy || !canUseOcr} onClick={() => desktopAvailable ? void pickImage() : fileInput.current?.click()}>
                  <FileImage size={15} aria-hidden="true" />
                </DesktopIconButton>
              {renderOpenPathAction(imageSource?.kind === "path" ? imageSource.filePath : "", "打开图片位置", showError)}
            </>
          }
        />
        <div className="toolbar-actions smart-ocr-action-bar">
          <button className="icon-button" type="button" title="粘贴图片" aria-label="粘贴图片" disabled={isBusy || !canUseOcr} onClick={() => void pasteImage()}>
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
        <InlineNotice tone="info" title="正在检查智能识别组件">确认 OCR 运行组件状态后才能开始识别。</InlineNotice>
      ) : null}
      {ocrPermission.canOperate && healthQuery.isError ? (
        <InlineNotice tone="error" title="无法确认智能识别组件状态">暂不能安全启用 OCR，请恢复业务服务后重试。</InlineNotice>
      ) : null}
      {ocrPermission.canOperate && ocrRuntimeUnavailable ? (
        <InlineNotice tone="warning" title="智能识别暂不可用">
          {readOcrRuntimeUnavailableMessage(ocrRuntime?.status)}
        </InlineNotice>
      ) : null}
      {message ? <InlineNotice tone={messageType}>{message}</InlineNotice> : null}

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
            tabIndex={0}
            role="region"
            className={isDraggingPreview ? "smart-ocr-preview-viewport smart-ocr-preview-viewport-dragging" : "smart-ocr-preview-viewport"}
            onWheel={handlePreviewWheel}
            onPointerDown={beginPreviewDrag}
            onPointerMove={movePreviewDrag}
            onPointerUp={endPreviewDrag}
            onPointerCancel={endPreviewDrag}
            onLostPointerCapture={() => { previewDragRef.current = null; setIsDraggingPreview(false); }}
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
                  onError={() => showError("图片无法预览，仍可尝试识别或改用 PNG/JPEG 图片。")}
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
            <textarea aria-label="识别文本" value={recognizedText || (isBusy ? "正在识别中，请稍候..." : "")} readOnly />
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
