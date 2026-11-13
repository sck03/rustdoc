import type { BusinessAttachmentRevisionRecord, ExportDocManagerApiClient } from "../../api/index.ts";
import { isDesktopBridgeAvailable, selectSaveFilePath } from "../../desktop/desktopBridge.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";

export async function readBusinessAttachment(client: ExportDocManagerApiClient, id: number,
  version: BusinessAttachmentRevisionRecord, showPreview: boolean, signal: AbortSignal) {
  if (!showPreview && isDesktopBridgeAvailable()) {
    const destinationPath = await selectSaveFilePath(version.fileName);
    if (!destinationPath || signal.aborted) return null;
    await client.saveBusinessAttachmentToPath({ id, revision: version.revision, body: { destinationPath } }, { signal });
    return { saved: true, preview: null };
  }
  const blob = await client.downloadBusinessAttachment({ id, revision: version.revision }, { signal });
  if (!showPreview) downloadBlob(blob, version.fileName);
  return { saved: !showPreview, preview: showPreview ? { id, blob, name: version.fileName } : null };
}
