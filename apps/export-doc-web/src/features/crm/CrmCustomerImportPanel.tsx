import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { OperationFeedback } from "../../ui/OperationFeedback.tsx";
import { DirectoryImportPreview } from "../../ui/DirectoryImportPreview.tsx";
import { useFileImportWorkspace } from "../../ui/useFileImportWorkspace.ts";

export function CrmCustomerImportPanel({ client, canImport, onImported }: {
  client: ExportDocManagerApiClient;
  canImport: boolean;
  onImported: (signal: AbortSignal) => Promise<void>;
}) {
  const { preview, busy, importing, feedback, select: selectFile, commit: confirmImport } = useFileImportWorkspace({
    owner: client, enabled: canImport,
    preview: (file, signal) => client.previewCrmCustomerImport({ fileName: file.name, body: file }, { signal }),
    commit: (value, signal) => client.importCrmCustomers({ body: { previewId: value.previewId } }, { signal }),
    describe: result => `已导入 ${result.createdCustomers} 家客户、${result.createdContacts} 位联系人，跳过 ${result.skippedDuplicates} 行。`,
    onImported,
  });

  return <section className="form-section">
    <div className="section-header"><h3>客户文件导入</h3><span>CSV / XLSX，最多 5000 行、10 MB</span></div>
    <div className="form-actions">
      <label className={`secondary-button${canImport ? "" : " disabled"}`}>选择文件<input type="file" hidden disabled={!canImport || importing} accept=".csv,.xlsx,.xlsm" onChange={(event) => { const file = event.currentTarget.files?.[0]; event.currentTarget.value = ""; selectFile(file); }} /></label>
      <button className="primary-button" type="button" disabled={!canImport || busy || !preview?.validRows} onClick={() => void confirmImport()}>{busy ? "处理中..." : "确认导入有效行"}</button>
    </div>
    <OperationFeedback feedback={feedback} />
    {preview ? <>
      <p>共 {preview.totalRows} 行，有效 {preview.validRows} 行，重复 {preview.duplicateRows} 行。新客户以潜在客户状态导入。</p>
      <DirectoryImportPreview key={preview.previewId} rows={preview.rows} busy={busy} />
    </> : null}
  </section>;
}
