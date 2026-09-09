import { useEffect, useRef, useState } from "react";
import { Upload } from "lucide-react";
import type { ApiReportTemplateImageResourceResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { isAbortError } from "../../ui/useAbortableOperation.ts";
import { CommitTextField, DesignerCheckbox as CheckRow } from "./ReportDesignerPropertyControls.tsx";
import { SelectField } from "./ReportDesignerV3InspectorControls.tsx";
import type { ReportDesignerV3Element, ReportDesignerV3ImageResource } from "./reportDesignerV3Schema.ts";
import type { ReportDesignerReportType } from "./reportDesignerSchema.ts";
import { getControlledReportImageFieldPaths, isControlledReportImageFieldPath } from "./reportDesignerSchemaDomains.ts";

export function ImageSourceEditor({ element, reportType, resources, editable, client, onPatch, onUploaded }: { element: Extract<ReportDesignerV3Element, { type: "Image" }>; reportType: ReportDesignerReportType; resources: ReportDesignerV3ImageResource[]; editable: boolean; client?: ExportDocManagerApiClient; onPatch: (update: Partial<ReportDesignerV3Element>) => void; onUploaded: (elementId: string, resource: ApiReportTemplateImageResourceResponse) => void }) {
  const inputRef = useRef<HTMLInputElement>(null);
  const uploadRef = useRef<AbortController | null>(null);
  const [uploading, setUploading] = useState(false);
  const [feedback, setFeedback] = useState<{ tone: "success" | "error"; text: string } | null>(null);
  useEffect(() => () => uploadRef.current?.abort(), [element.id, client]);
  const imageFields = getControlledReportImageFieldPaths(reportType);
  const currentImageField = isControlledReportImageFieldPath(element.fieldPath) ? element.fieldPath : "";
  const imageFieldOptions = [
    { value: "", label: "请选择受控图片字段" },
    ...imageFields.map((fieldPath) => ({ value: fieldPath, label: fieldPath })),
  ];
  const resourceOptions = [
    { value: "", label: resources.length ? "请选择已上传图片" : "暂无已上传图片" },
    ...resources.map((resource) => ({
      value: resource.id,
      label: `${resource.altText || "图片"} · ${formatResourceSize(resource.byteLength)} · ${resource.id.slice(0, 16)}…`,
    })),
  ];

  async function upload(file: File) {
    if (!client || !editable || uploadRef.current) return;
    if (file.size > 32 * 1024 * 1024) {
      setFeedback({ tone: "error", text: "图片不能超过 32 MB。" });
      return;
    }
    setUploading(true);
    setFeedback(null);
    const controller = new AbortController();
    uploadRef.current = controller;
    try {
      const resource = await client.uploadReportTemplateV3ImageResource({
        fileName: file.name,
        mediaType: file.type || undefined,
        body: file,
      }, { signal: controller.signal });
      if (controller.signal.aborted) return;
      onUploaded(element.id, resource);
      setFeedback({ tone: "success", text: "图片已上传并自动绑定，无需填写资源 ID。" });
    } catch (error) {
      if (!controller.signal.aborted && !isAbortError(error)) setFeedback({ tone: "error", text: readApiError(error) });
    } finally {
      uploadRef.current = null;
      if (!controller.signal.aborted) setUploading(false);
    }
  }

  return (
    <div className="report-designer-v3-image-editor">
      <SelectField label="来源" value={element.sourceKind} options={[{ value: "Field", label: "字段图片" }, { value: "Resource", label: "上传图片" }]} disabled={!editable || uploading} onChange={(value) => {
        const sourceKind = value === "Resource" ? "Resource" : "Field";
        onPatch(sourceKind === "Field"
          ? { sourceKind, fieldPath: currentImageField || imageFields[0], resourceId: undefined }
          : { sourceKind, fieldPath: undefined, resourceId: element.resourceId || resources[0]?.id });
      }} />
      {element.sourceKind === "Field" ? (
        <>
          <SelectField label="图片字段" value={element.fieldPath ?? ""} options={imageFieldOptions} disabled={!editable || imageFields.length === 0} onChange={(fieldPath) => onPatch({ fieldPath: fieldPath || undefined })} />
          {imageFields.length === 0 ? <small className="report-designer-v3-muted">当前报表类型没有可绑定的受控图片字段。</small> : null}
        </>
      ) : (
        <div className="report-designer-v3-resource-picker">
          <SelectField label="已上传图片" value={element.resourceId ?? ""} options={resourceOptions} disabled={!editable || uploading || resources.length === 0} onChange={(resourceId) => onPatch({ resourceId: resourceId || undefined })} />
          <input ref={inputRef} type="file" hidden accept="image/png,image/jpeg,image/gif,image/webp,.png,.jpg,.jpeg,.gif,.webp" onChange={(event) => {
            const file = event.currentTarget.files?.[0];
            event.currentTarget.value = "";
            if (file) void upload(file);
          }} />
          <button className="command-button secondary report-designer-v3-upload-button" type="button" disabled={!editable || uploading || !client} onClick={() => inputRef.current?.click()}>
            <Upload size={15} aria-hidden="true" />
            <span>{uploading ? "正在上传…" : "选择图片并上传"}</span>
          </button>
          <small className="report-designer-v3-resource-help">支持 PNG、JPEG、GIF、WebP，最大 32 MB；上传后自动生成并绑定受控资源。</small>
          {element.resourceId ? <div className="report-designer-v3-resource-id"><span>资源 ID</span><code>{element.resourceId}</code></div> : null}
          {feedback ? <div className={`report-designer-v3-upload-feedback is-${feedback.tone}`} role={feedback.tone === "error" ? "alert" : "status"}>{feedback.text}</div> : null}
        </div>
      )}
      <label><span>替代文本</span><CommitTextField value={element.altText ?? ""} disabled={!editable} placeholder="例如：公司标志" onCommit={(altText) => onPatch({ altText: altText || undefined })} /></label>
      <CheckRow checked={element.hideWhenSourceEmpty} disabled={!editable} onChange={(hideWhenSourceEmpty) => onPatch({ hideWhenSourceEmpty })}>来源为空时隐藏</CheckRow>
    </div>
  );
}

function formatResourceSize(value?: number) {
  if (!value || value <= 0) return "未知大小";
  return value >= 1024 * 1024 ? `${(value / (1024 * 1024)).toFixed(1)} MB` : `${Math.ceil(value / 1024)} KB`;
}
