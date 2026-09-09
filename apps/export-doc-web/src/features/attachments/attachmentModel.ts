import type { ApiAttachmentUploadForm, BusinessAttachmentRecord, UploadBusinessAttachmentRequest } from "../../api/index.ts";

export type AttachmentUploadInput = Omit<ApiAttachmentUploadForm, "file"> & { file: File; invoiceId: number };

export function attachmentUploadRequest(input: AttachmentUploadInput): UploadBusinessAttachmentRequest {
  const { invoiceId, ...fields } = input;
  const body = new FormData();
  for (const [key, value] of Object.entries(fields)) {
    if (value !== undefined && value !== null) body.append(key, value instanceof File ? value : String(value));
  }
  return { invoiceId, body };
}

export type AttachmentMetadata = Pick<BusinessAttachmentRecord, "title" | "categoryId" | "poNumber" | "styleNo">;

export function attachmentMetadata(item?: AttachmentMetadata, categoryId = 0): AttachmentMetadata {
  return { title: item?.title ?? "", categoryId: item?.categoryId ?? categoryId, poNumber: item?.poNumber ?? "", styleNo: item?.styleNo ?? "" };
}

export function attachmentVersionLabel(item: BusinessAttachmentRecord) {
  if (item.isArchived) return "已停用（保留全部版本）";
  if (!item.currentRevision) return `待确认 · 已上传 v${item.latestRevision}`;
  return `有效版本 v${item.currentRevision}${item.latestRevision > item.currentRevision ? ` · 新版本 v${item.latestRevision} 待确认` : ""}`;
}

export function fileSizeLabel(bytes: number) {
  return bytes < 1024 * 1024 ? `${Math.ceil(bytes / 1024)} KiB` : `${(bytes / 1024 / 1024).toFixed(1)} MiB`;
}

export function canPreviewAttachment(contentType: string) {
  return ["application/pdf", "image/png", "image/jpeg", "image/gif", "image/webp"].includes(contentType);
}
