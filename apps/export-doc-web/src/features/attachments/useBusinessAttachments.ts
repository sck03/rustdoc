import { useEffect, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { BusinessAttachmentRevisionRecord, BusinessAttachmentUpdate, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, selectSaveFilePath } from "../../desktop/desktopBridge.ts";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { attachmentUploadRequest, type AttachmentUploadInput } from "./attachmentModel.ts";

export function useBusinessAttachments(client: ExportDocManagerApiClient, invoiceId: number | undefined, userId: number) {
  const [keyword, setKeyword] = useState("");
  const [search, setSearch] = useState("");
  const [includeArchived, setIncludeArchived] = useState(false);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [uploadMode, setUploadMode] = useState<"new" | "revision" | null>(null);
  const [preview, setPreview] = useState<{ blob: Blob; name: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const inFlight = useRef(false);
  const abortable = useAbortableOperation();
  const queries = useQueryClient();
  const query = useQuery({
    queryKey: ["business-attachments", userId, invoiceId, search, includeArchived, pageNumber, pageSize],
    queryFn: ({ signal }) => client.listBusinessAttachments({ invoiceId, keyword: search || undefined, includeArchived, pageNumber, pageSize }, { signal }),
  });
  const details = useQuery({
    queryKey: ["business-attachments", userId, "detail", selectedId],
    queryFn: ({ signal }) => client.getBusinessAttachment({ id: selectedId! }, { signal }), enabled: selectedId !== null,
  });
  const invoice = useQuery({
    queryKey: queryKeys.invoice(invoiceId ?? 0), queryFn: ({ signal }) => client.getInvoice({ id: invoiceId! }, { signal }), enabled: invoiceId !== undefined,
  });
  useEffect(() => { setPreview(null); }, [selectedId]);
  useEffect(() => {
    if (query.data && pageNumber > Math.max(1, query.data.page.totalPages)) setPageNumber(Math.max(1, query.data.page.totalPages));
  }, [query.data, pageNumber]);
  async function run<T>(operation: (signal: AbortSignal) => Promise<T>, onSuccess: (result: T) => void, write = false) {
    if (inFlight.current) return false;
    inFlight.current = true; setBusy(true); setError(""); setMessage("");
    try {
      const result = await abortable(operation);
      if (write) await queries.invalidateQueries({ queryKey: ["business-attachments"] });
      onSuccess(result);
      return true;
    } catch (failure) {
      if (!isAbortError(failure)) {
        setError(readApiError(failure));
        if (write) void queries.invalidateQueries({ queryKey: ["business-attachments"] });
      }
      return false;
    } finally { inFlight.current = false; setBusy(false); }
  }
  function upload(request: AttachmentUploadInput) {
    return run((signal) => client.uploadBusinessAttachment(attachmentUploadRequest(request), { signal }), (item) => {
      setSelectedId(item.id);
      setUploadMode(null);
      setMessage(`v${item.latestRevision} 已归档，请核对后确认有效版本。`);
    }, true);
  }
  function update(request: BusinessAttachmentUpdate) {
    if (!details.data) return Promise.resolve(false);
    const id = details.data.attachment.id;
    return run((signal) => client.updateBusinessAttachment({ id, body: request }, { signal }),
      () => setMessage("资料状态已更新。"), true);
  }
  async function read(version: BusinessAttachmentRevisionRecord, showPreview: boolean) {
    if (!details.data) return;
    const id = details.data.attachment.id;
    if (!showPreview && isDesktopBridgeAvailable()) {
      await run(async (signal) => {
        const destinationPath = await selectSaveFilePath(version.fileName);
        if (!destinationPath || signal.aborted) return false;
        await client.saveBusinessAttachmentToPath({ id, revision: version.revision, body: { destinationPath } }, { signal });
        return true;
      }, (saved) => { if (saved) setMessage("归档原文件已保存。"); });
    } else {
      await run((signal) => client.downloadBusinessAttachment({ id, revision: version.revision }, { signal }), (blob) => {
        if (showPreview) setPreview({ blob, name: version.fileName });
        else downloadBlob(blob, version.fileName);
      });
    }
  }
  const commitSearch = () => { setSearch(keyword.trim()); setPageNumber(1); };
  return { query, details, invoice, keyword, search, includeArchived, pageNumber, pageSize, selectedId, busy, error, message,
    uploadMode, setUploadMode, preview, setPreview, upload, update, read, setSelectedId, setPageNumber, commitSearch,
    changeKeyword: (value: string) => { setKeyword(value); if (!value) { setSearch(""); setPageNumber(1); } },
    changeArchived: (value: boolean) => { setIncludeArchived(value); setPageNumber(1); },
    changePageSize: (value: number) => { setPageSize(value); setPageNumber(1); },
    refresh: () => { if (keyword.trim() !== search) commitSearch(); else void queries.invalidateQueries({ queryKey: ["business-attachments"] }); } };
}
