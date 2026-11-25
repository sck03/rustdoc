import type { BusinessAttachmentRevisionRecord, ExportDocManagerApiClient } from "../../api/index.ts";
import { isDesktopBridgeAvailable, selectSaveFilePath } from "../../desktop/desktopBridge.ts";
import { canPreviewAttachment } from "./attachmentModel.ts";
import { startDownloadFromTicket } from "../../ui/downloadJobResult.ts";

export async function readBusinessAttachment(client: ExportDocManagerApiClient, id: number,
  version: BusinessAttachmentRevisionRecord, showPreview: boolean, signal: AbortSignal) {
  if (!showPreview && isDesktopBridgeAvailable()) {
    const destinationPath = await selectSaveFilePath(version.fileName);
    if (!destinationPath || signal.aborted) return null;
    await client.saveBusinessAttachmentToPath({ id, revision: version.revision, body: { destinationPath } }, { signal });
    return { saved: true, preview: null };
  }
  if (!showPreview) {
    const ticket = await client.createBusinessAttachmentDownloadTicket({ id, revision: version.revision }, { signal });
    signal.throwIfAborted();
    startDownloadFromTicket(client, ticket, version.fileName);
    return { saved: true, browserDownload: true, preview: null };
  }
  if (!canPreviewAttachment(version.contentType, version.length)) {
    throw new Error("在线预览支持 16 MiB 以内的 PDF 和图片，请下载后查看此文件。");
  }
  const blob = await client.downloadBusinessAttachment({ id, revision: version.revision }, { signal });
  signal.throwIfAborted();
  return { saved: false, preview: { id, blob, name: version.fileName } };
}
