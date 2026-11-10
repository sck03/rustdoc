import { type FormEvent, useLayoutEffect, useRef, useState } from "react";
import type { QueryClient } from "@tanstack/react-query";
import type { NavigateFunction } from "react-router-dom";
import type { ApiInvoiceListItemDto, ExportDocManagerApiClient } from "../../../api/index.ts";
import { queryKeys } from "../../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, openPath, selectExcelFile } from "../../../desktop/desktopBridge.ts";
import { downloadBlob } from "../../../ui/downloadBlob.ts";
import { readApiError } from "../../../ui/formUtils.ts";
import { isAbortError } from "../../../ui/useAbortableOperation.ts";
import { useScopedSave } from "../../../ui/useScopedSave.ts";
import { requestPackageOpenPath, requestPackageSavePath } from "../invoiceListDesktopPaths.ts";
import { sanitizePackageFileName } from "../invoiceListFileNames.ts";
import { buildDefaultCopyInvoiceNo, buildExcelImportRouteSuccessMessage, createEmptyInvoiceTransferImportDraft, createInvoiceTransferImportDraft, normalizeRequiredPackagePath, validateInvoiceCopyDraft, validateInvoiceTransferImportDraft, type InvoiceTransferImportDraft, type InvoiceCopyDraft } from "../invoiceListModels.ts";

type Options = { client: ExportDocManagerApiClient; queryClient: QueryClient; navigate: NavigateFunction; defaultExportDirectory: string; canOperate: boolean; canImportExcel: boolean; canExport: boolean };
type Context = { signal: AbortSignal; checkCurrent: () => void };
type FileAction = "preview" | "import" | "export" | "excel" | "open";

/** List actions own their dialogs, temporary files and navigation lifetime. */
export function useInvoiceListActions({ client, queryClient, navigate, defaultExportDirectory, canOperate, canImportExcel, canExport }: Options) {
  const [copyDraft, setCopyDraft] = useState<InvoiceCopyDraft | null>(null);
  const [copyMessage, setCopyMessage] = useState<string | null>(null);
  const [transferDraft, setTransferDraft] = useState<InvoiceTransferImportDraft | null>(null);
  const [transferUploadFile, setTransferUploadFile] = useState<File | null>(null);
  const [transferMessage, setTransferMessage] = useState<string | null>(null);
  const [transferSuccessMessage, setTransferSuccessMessage] = useState<string | null>(null);
  const [lastExportedPackagePath, setLastExportedPackagePath] = useState<string | null>(null);
  const excelImportInputRef = useRef<HTMLInputElement>(null);
  const [excelSchemeName, setExcelSchemeName] = useState("");
  useLayoutEffect(() => setExcelSchemeName(""), [client]);
  const copy = useScopedSave(String(canOperate), client);
  const file = useScopedSave(`${canOperate}:${canImportExcel}:${canExport}`, client);
  const [fileAction, setFileAction] = useState<FileAction>("preview");
  const refresh = () => Promise.all([queryKeys.invoicesRoot(), queryKeys.queryInvoicesRoot(), queryKeys.dashboard()].map(queryKey => queryClient.invalidateQueries({ queryKey })));

  function closeCopyPanel() { copy.cancel(); setCopyDraft(null); setCopyMessage(null); }
  function openCopyPanel(invoice: ApiInvoiceListItemDto) {
    if (!canOperate) return;
    closeCopyPanel();
    setCopyDraft({ source: invoice, newInvoiceNo: buildDefaultCopyInvoiceNo(invoice.invoiceNo), copyHeader: true, copyItems: true, resetDates: true, clearAmounts: false });
  }
  function patchCopyDraft(next: Partial<InvoiceCopyDraft>) { setCopyDraft(current => current ? { ...current, ...next } : null); setCopyMessage(null); }
  async function submitCopyInvoice(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canOperate || !copyDraft) return;
    const validation = validateInvoiceCopyDraft(copyDraft);
    if (validation) { setCopyMessage(validation); return; }
    try {
      await copy.run(async ({ signal, checkCurrent }) => {
        setCopyMessage(null);
        const response = await client.cloneInvoice({ id: copyDraft.source.id, body: { newInvoiceNo: copyDraft.newInvoiceNo.trim(), options: { copyHeader: copyDraft.copyHeader, copyItems: copyDraft.copyItems, resetDates: copyDraft.resetDates, clearAmounts: copyDraft.clearAmounts } } }, { signal });
        checkCurrent(); setCopyDraft(null);
        queryClient.setQueryData(queryKeys.invoice(response.id), response.invoice);
        await refresh(); checkCurrent();
        navigate(`/invoices/${response.id}`, { state: { successMessage: response.message || "发票已复制。" } });
      });
    } catch (error) { if (!isAbortError(error)) setCopyMessage(readApiError(error)); }
  }

  function clearFileResult() { setTransferMessage(null); setTransferSuccessMessage(null); setLastExportedPackagePath(null); }
  function closeTransferPanel() { file.cancel(); setTransferDraft(null); setTransferUploadFile(null); setTransferMessage(null); }
  async function runFile(action: FileAction, work: (context: Context) => Promise<void>) {
    try {
      await file.run(async context => { setFileAction(action); clearFileResult(); await work(context); });
    } catch (error) { if (!isAbortError(error)) setTransferMessage(readApiError(error)); }
  }
  async function previewTransfer(packagePath: string, uploadFile?: File) {
    if (!canOperate || (file.isSaving && fileAction !== "preview")) return;
    file.cancel();
    setTransferUploadFile(uploadFile ?? null);
    setTransferDraft({ ...createEmptyInvoiceTransferImportDraft(), packagePath });
    await runFile("preview", async ({ signal, checkCurrent }) => {
      const response = uploadFile
        ? await client.previewUploadedInvoiceTransferPackage({ fileName: uploadFile.name, body: uploadFile }, { signal })
        : await client.previewInvoiceTransferPackage({ body: { packagePath } }, { signal });
      checkCurrent(); setTransferDraft(createInvoiceTransferImportDraft(packagePath, response));
    });
  }
  async function handleChooseImportTransferPackage() {
    if (!canOperate || file.isSaving) return;
    closeTransferPanel(); clearFileResult();
    if (!isDesktopBridgeAvailable()) { setTransferDraft(createEmptyInvoiceTransferImportDraft()); return; }
    // Keep the native picker in the same cancellable operation as its preview.
    await runFile("preview", async ({ signal, checkCurrent }) => {
      const packagePath = await requestPackageOpenPath();
      checkCurrent(); if (!packagePath) return;
      setTransferDraft({ ...createEmptyInvoiceTransferImportDraft(), packagePath });
      const response = await client.previewInvoiceTransferPackage({ body: { packagePath } }, { signal });
      checkCurrent(); setTransferDraft(createInvoiceTransferImportDraft(packagePath, response));
    });
  }
  function handlePreviewTransferPackage(packagePath: string) {
    if (!canOperate) return;
    const normalized = normalizeRequiredPackagePath(packagePath);
    if (normalized.error) { setTransferMessage(normalized.error); return; }
    void previewTransfer(normalized.value);
  }
  function handlePreviewTransferUpload(uploadFile: File | null) {
    if (!canOperate || (file.isSaving && fileAction !== "preview")) return;
    if (uploadFile) void previewTransfer(uploadFile.name, uploadFile);
    else { closeTransferPanel(); setTransferDraft(createEmptyInvoiceTransferImportDraft()); }
  }
  function patchTransferDraft(next: Partial<InvoiceTransferImportDraft>) { setTransferDraft(current => current ? { ...current, ...next } : null); clearFileResult(); }
  async function submitTransferImport(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canOperate || !transferDraft) return;
    const validation = validateInvoiceTransferImportDraft(transferDraft);
    if (validation) { setTransferMessage(validation); return; }
    await runFile("import", async ({ signal, checkCurrent }) => {
      const draft = transferDraft;
      const response = isDesktopBridgeAvailable()
        ? await client.importInvoiceTransferPackage({ body: { packagePath: draft.packagePath.trim(), conflictAction: draft.conflictAction, newInvoiceNo: draft.newInvoiceNo.trim() } }, { signal })
        : await client.importUploadedInvoiceTransferPackage({ fileName: transferUploadFile?.name, conflictAction: draft.conflictAction, newInvoiceNo: draft.newInvoiceNo.trim() || undefined, body: transferUploadFile ?? new Blob() }, { signal });
      checkCurrent(); setTransferDraft(null); setTransferUploadFile(null); setTransferSuccessMessage(response.message || "单据包已导入。");
      await refresh(); checkCurrent();
      if (response.result.invoiceId && response.result.invoiceId > 0) navigate(`/invoices/${response.result.invoiceId}`, { state: { successMessage: response.message || "单据包已导入。" } });
    });
  }
  async function handleExportTransferPackage(invoice: ApiInvoiceListItemDto) {
    if (!canExport) return;
    await runFile("export", async ({ signal, checkCurrent }) => {
      const name = `${sanitizePackageFileName(invoice.invoiceNo || "invoice")}.edpkg`;
      if (isDesktopBridgeAvailable()) {
        const packagePath = await requestPackageSavePath(name, defaultExportDirectory);
        checkCurrent(); if (!packagePath) return;
        const response = await client.saveInvoiceTransferPackageToPath({ id: invoice.id, body: { packagePath } }, { signal });
        checkCurrent(); setTransferSuccessMessage(`${response.message} ${response.packagePath}`); setLastExportedPackagePath(response.packagePath);
      } else {
        const blob = await client.downloadInvoiceTransferPackage({ id: invoice.id }, { signal });
        checkCurrent(); downloadBlob(blob, name); setTransferSuccessMessage("发票单据包已交给浏览器下载。");
      }
    });
  }
  async function importExcel(uploadFile?: File) {
    if (!canOperate || !canImportExcel) return;
    await runFile("excel", async ({ signal, checkCurrent }) => {
      const filePath = uploadFile ? "" : await selectExcelFile();
      checkCurrent(); if (!uploadFile && !filePath) return;
      const response = uploadFile
        ? await client.previewUploadedExcelImport({ fileName: uploadFile.name, schemeName: excelSchemeName || undefined, body: uploadFile }, { signal })
        : await client.previewExcelImport({ body: { filePath: filePath ?? "", schemeName: excelSchemeName || undefined } }, { signal });
      checkCurrent();
      if (!response.invoice) throw new Error("Excel 已解析，但没有生成可用的发票草稿。请检查模板内容。");
      setTransferDraft(null); setTransferUploadFile(null);
      navigate("/invoices/new", { state: { invoiceDraft: response.invoice, successMessage: buildExcelImportRouteSuccessMessage(response) } });
    });
  }
  function handleChooseExcelImport() {
    if (!canOperate || !canImportExcel || file.isSaving) return;
    if (isDesktopBridgeAvailable()) void importExcel();
    else { clearFileResult(); excelImportInputRef.current?.click(); }
  }
  function handleExcelImportUpload(uploadFile: File | undefined) { if (uploadFile) void importExcel(uploadFile); }
  async function handleOpenLastExportedPackage() {
    if (!lastExportedPackagePath) return;
    const path = lastExportedPackagePath;
    await runFile("open", async ({ checkCurrent }) => { setLastExportedPackagePath(path); await openPath(path); checkCurrent(); });
  }
  return { excelSchemeName, setExcelSchemeName, copyDraft, closeCopyPanel, openCopyPanel, patchCopyDraft, submitCopyInvoice, copyBusy: copy.isSaving,
    transferDraft, closeTransferPanel, transferUploadFile, transferSuccessMessage, lastExportedPackagePath, excelImportInputRef,
    transferBusy: file.isSaving, handleChooseImportTransferPackage, handlePreviewTransferPackage, handlePreviewTransferUpload,
    patchTransferDraft, submitTransferImport, handleExportTransferPackage, handleChooseExcelImport, handleExcelImportUpload, handleOpenLastExportedPackage,
    actionMessage: copyMessage ?? transferMessage, actionsBusy: copy.isSaving || file.isSaving };
}
