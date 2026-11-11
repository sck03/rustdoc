import { useLayoutEffect, useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ApiSingleWindowExcelImportPreviewResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { useScopedSave } from "../../ui/useScopedSave.ts";
import { useTransientPreview } from "../../ui/useTransientPreview.ts";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { isAbortError } from "../../ui/useAbortableOperation.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { downloadExcelTemplate } from "../tools/excel/downloadExcelTemplate.ts";

const rootKey = ["single-window", "sources"] as const;
export function useSingleWindowSources(client: ExportDocManagerApiClient) {
  const permission = useModulePermission("document.single-window");
  const jobs = useModulePermission("document.jobs");
  const canDownloadTemplate = permission.canOperate && jobs.canView && jobs.canOperate;
  const owner = useMemo(() => ({}), [client, permission.canOperate]);
  const cache = useQueryClient();
  const [referenceNo, setReferenceNo] = useState("");
  const [fileName, setFileName] = useState("");
  const [schemeName, setSchemeName] = useState("");
  const previewOwner = useMemo(() => ({}), [owner, schemeName]);
  const [keyword, setKeyword] = useState("");
  const [page, setPage] = useState(1);
  const [message, setMessage] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const save = useScopedSave(`single-window-source:${canDownloadTemplate}`, owner);
  const preview = useTransientPreview<ApiSingleWindowExcelImportPreviewResponse, File>(previewOwner,
    (signal, file) => client.previewSingleWindowExcelImport({ fileName: file.name, schemeName: schemeName || undefined, body: file }, { signal }),
    error => setMessage(readApiError(error)));
  const query = useQuery({ queryKey: [...rootKey, page, keyword],
    queryFn: ({ signal }) => client.listSingleWindowSources({ pageNumber: page, pageSize: 50, keyword }, { signal }),
    enabled: permission.canView });
  useServerPageNumber(query, page, setPage, 50);
  useLayoutEffect(() => {
    setReferenceNo(""); setFileName(""); setSchemeName(""); setMessage(null); setSuccess(null);
  }, [owner]);
  useUnsavedChangesGuard({ isDirty: Boolean(referenceNo.trim() || fileName), message: "申报资料尚未创建。" });
  const busy = save.isSaving || preview.isPending;
  async function create() {
    if (!permission.canOperate || busy || !referenceNo.trim()) return;
    if (fileName && (!preview.preview?.result.success || !preview.preview.result.invoice)) {
      setMessage("请重新选择可识别的 Excel，或清除文件后手工录入。"); return;
    }
    setMessage(null); setSuccess(null);
    try {
      await save.run(async ({ signal, checkCurrent }) => {
        await client.createSingleWindowSource({ body: { referenceNo: referenceNo.trim(),
          ...(preview.preview?.result.invoice ? { invoice: preview.preview.result.invoice, coo: preview.preview.coo } : {}) } }, { signal });
        checkCurrent(); setReferenceNo(""); clearFile(); setSchemeName("");
        setSuccess("申报资料已创建，可分别填写代理委托和原产地证。");
        try { await cache.invalidateQueries({ queryKey: rootKey }); }
        catch (error) { checkCurrent(); setMessage(`已创建，但列表刷新失败：${readApiError(error)}`); }
      });
    } catch (error) { if (!isAbortError(error)) setMessage(readApiError(error)); }
  }
  function select(file?: File) {
    if (!file || busy || !permission.canOperate) return;
    setFileName(file.name); setMessage(null); setSuccess(null); void preview.generate(file);
  }
  function clearFile() { setFileName(""); preview.clear(); }
  async function downloadTemplate() {
    if (!canDownloadTemplate || busy) return;
    try {
      await save.run(async context => {
        const result = await downloadExcelTemplate(client, "coo", context);
        if (result) { setMessage(null); setSuccess(result); }
      });
    } catch (error) { if (!isAbortError(error)) setMessage(readApiError(error)); }
  }
  return { permission, referenceNo, setReferenceNo, keyword, setKeyword, page, setPage,
    message, success, busy, preview, query, create, select, fileName, clearFile, downloadTemplate, canDownloadTemplate,
    schemeName, changeScheme: (value: string) => { if (!busy) { setSchemeName(value); clearFile(); } } };
}
