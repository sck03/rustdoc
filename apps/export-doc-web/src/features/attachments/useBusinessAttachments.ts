import { useEffect, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { BusinessAttachmentDelete, BusinessAttachmentMetadataUpdate, BusinessAttachmentRecord, BusinessAttachmentRevisionRecord, BusinessAttachmentUpdate, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, selectSaveFilePath } from "../../desktop/desktopBridge.ts";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { attachmentUploadRequest, type AttachmentUploadInput } from "./attachmentModel.ts";
import { useBusinessAttachmentCategories } from "./useBusinessAttachmentCategories.ts";
import { useDirectoryLocation } from "../../ui/useDirectoryLocation.ts";
import { useRouteQuery } from "../../ui/useRouteQuery.ts";
import { readRouteId } from "../../ui/routeQueryState.ts";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";

export function useBusinessAttachments(client: ExportDocManagerApiClient, invoiceId: number | undefined, userId: number) {
  const { keywordInput: keyword, setKeywordInput: setKeyword, keyword: search, setKeyword: setSearch, pageNumber, setPageNumber, pageSize, setPageSize } = useDirectoryLocation();
  const { params, update: updateRoute } = useRouteQuery();
  const includeArchived = params.get("archived") === "true";
  const setIncludeArchived = (value: boolean) => updateRoute({ archived: value, page: null });
  const selectedId = readRouteId(params.get("attachmentId"));
  const setSelectedId = (id: number | null) => updateRoute({ attachmentId: id }, false);
  const [uploadMode, setUploadModeState] = useState<"new" | "revision" | null>(() => invoiceId !== undefined && params.get("upload") === "new" ? "new" : null);
  function setUploadMode(mode: "new" | "revision" | null) {
    setUploadModeState(mode);
    if (mode === null && params.has("upload")) updateRoute({ upload: null });
  }
  const [preview, setPreview] = useState<{ id: number; blob: Blob; name: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const inFlight = useRef(false);
  const abortable = useAbortableOperation(selectedId);
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
  const categoryInvoiceId = invoiceId ?? details.data?.attachment.invoiceId;
  const { categories, saveCategory, removeCategory } = useBusinessAttachmentCategories(client, userId, categoryInvoiceId, run, setMessage);
  useEffect(() => { setPreview(null); }, [selectedId]);
  useServerPageNumber({ ...query, data: query.data?.page }, pageNumber, setPageNumber);
  async function run<T>(operation: (signal: AbortSignal) => Promise<T>, onSuccess: (result: T) => void, write = false) {
    if (inFlight.current) return false;
    inFlight.current = true; setBusy(true); setError(""); setMessage("");
    try {
      const result = await abortable(operation);
      onSuccess(result);
      if (write) await refreshRecords();
      return true;
    } catch (failure) {
      if (!isAbortError(failure)) {
        setError(readApiError(failure));
        if (write) void refreshRecords();
      }
      return false;
    } finally { inFlight.current = false; setBusy(false); }
  }
  const refreshRecords = () => Promise.all([
      queries.invalidateQueries({ queryKey: ["business-attachments"] }),
      queries.invalidateQueries({ queryKey: ["business-attachment-categories"] }),
    ]);
  const upload = (request: AttachmentUploadInput, signal: AbortSignal) => client.uploadBusinessAttachment(attachmentUploadRequest(request), { signal });
  function saveUploads(work: (signal: AbortSignal) => Promise<BusinessAttachmentRecord[]>) {
    return run(work, records => {
      setSelectedId(records.length === 1 ? records[0].id : null); setUploadMode(null);
      setMessage(`已归档 ${records.length} 份资料，请核对后确认有效版本。`);
    }, true);
  }
  function update(request: BusinessAttachmentUpdate) {
    if (!details.data) return Promise.resolve(false);
    const id = details.data.attachment.id;
    return run((signal) => client.updateBusinessAttachment({ id, body: request }, { signal }),
      () => setMessage("资料状态已更新。"), true);
  }
  function editMetadata(body: BusinessAttachmentMetadataUpdate) {
    if (!details.data) return Promise.resolve(false);
    return run((signal) => client.editBusinessAttachmentMetadata({ id: details.data.attachment.id, body }, { signal }),
      () => setMessage("资料信息已修正，历史文件完整保留。"), true);
  }
  function remove(body: BusinessAttachmentDelete) {
    if (!details.data) return Promise.resolve(false);
    return run((signal) => client.deleteBusinessAttachment({ id: details.data.attachment.id, body }, { signal }), () => {
      setSelectedId(null); setPreview(null); setMessage("业务资料及全部版本已删除，删除操作已记录。" );
    }, true);
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
        if (showPreview) setPreview({ id, blob, name: version.fileName });
        else downloadBlob(blob, version.fileName);
      });
    }
  }
  const commitSearch = () => { setSearch(keyword.trim()); setPageNumber(1); };
  return { query, details, invoice, categories, saveCategory, removeCategory, editMetadata, remove, keyword, search, includeArchived, pageNumber, pageSize, selectedId, busy, error, message,
    uploadMode, setUploadMode, preview: preview?.id === selectedId ? preview : null, setPreview, upload, saveUploads, update, read, setSelectedId, setPageNumber, commitSearch,
    changeKeyword: (value: string) => { setKeyword(value); if (!value) { setSearch(""); setPageNumber(1); } },
    changeArchived: setIncludeArchived,
    changePageSize: (value: number) => { setPageSize(value); setPageNumber(1); },
    refresh: () => { if (keyword.trim() !== search) commitSearch(); else void queries.invalidateQueries({ queryKey: ["business-attachments"] }); } };
}
