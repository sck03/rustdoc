import { useState } from "react";
import type { ApiCrmCustomerImportPreviewDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { OperationFeedback, errorFeedback, successFeedback, type OperationFeedbackState } from "../../ui/OperationFeedback.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";

export function CrmCustomerImportPanel({ client, canImport, onImported }: {
  client: ExportDocManagerApiClient;
  canImport: boolean;
  onImported: () => Promise<void>;
}) {
  const [preview, setPreview] = useState<ApiCrmCustomerImportPreviewDto | null>(null);
  const [busy, setBusy] = useState(false);
  const [feedback, setFeedback] = useState<OperationFeedbackState | null>(null);
  const runAbortableOperation = useAbortableOperation();

  async function selectFile(file?: File) {
    if (!canImport || !file) return;
    setBusy(true);
    try {
      setPreview(await runAbortableOperation((signal) => client.previewCrmCustomerImport(
        { fileName: file.name, body: file },
        { signal },
      )));
      setFeedback(null);
    } catch (error) {
      if (!isAbortError(error)) {
        setFeedback(errorFeedback(readApiError(error)));
        setPreview(null);
      }
    }
    finally { setBusy(false); }
  }

  async function confirmImport() {
    if (!canImport || !preview?.validRows) return;
    setBusy(true);
    try {
      const result = await runAbortableOperation((signal) => client.importCrmCustomers(
        { body: { previewId: preview.previewId } },
        { signal },
      ));
      setFeedback(successFeedback(`已导入 ${result.createdCustomers} 家客户、${result.createdContacts} 位联系人，跳过 ${result.skippedDuplicates} 行。`));
      setPreview(null);
      await onImported();
    } catch (error) {
      if (!isAbortError(error)) setFeedback(errorFeedback(readApiError(error)));
    }
    finally { setBusy(false); }
  }

  return <section className="form-section">
    <div className="section-header"><h3>客户文件导入</h3><span>CSV / XLSX，最多 5000 行、10 MB</span></div>
    <div className="form-actions">
      <label className={`secondary-button${canImport ? "" : " disabled"}`}>选择文件<input type="file" hidden disabled={!canImport} accept=".csv,.xlsx,.xlsm" onChange={(event) => { const file = event.currentTarget.files?.[0]; event.currentTarget.value = ""; void selectFile(file); }} /></label>
      <button className="primary-button" type="button" disabled={!canImport || busy || !preview?.validRows} onClick={() => void confirmImport()}>{busy ? "处理中..." : "确认导入有效行"}</button>
    </div>
    <OperationFeedback feedback={feedback} />
    {preview ? <>
      <p>共 {preview.totalRows} 行，有效 {preview.validRows} 行，重复 {preview.duplicateRows} 行。提交将使用服务端预检快照并重新校验；下表最多展示前 30 行。</p>
      <ResponsiveTableFrame label="CRM 客户导入预览" mobileLayout="scroll"><table className="data-table"><thead><tr><th>行</th><th>客户</th><th>国家</th><th>联系人</th><th>结果</th></tr></thead>
        <tbody>{preview.rows.slice(0, 30).map((row) => <tr key={row.rowNumber}><td>{row.rowNumber}</td><td>{row.name || "-"}</td><td>{row.countryRegion || "-"}</td><td>{row.contactName || "-"}</td><td>{row.error || (row.isDuplicate ? "重复，跳过" : "可导入")}</td></tr>)}</tbody>
      </table></ResponsiveTableFrame>
    </> : null}
  </section>;
}
