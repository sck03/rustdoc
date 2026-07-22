import { useState } from "react";
import type { ApiCrmCustomerImportPreviewDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { OperationFeedback, errorFeedback, successFeedback, type OperationFeedbackState } from "../../ui/OperationFeedback.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";

export function CrmCustomerImportPanel({ client, canOperate, onImported }: {
  client: ExportDocManagerApiClient;
  canOperate: boolean;
  onImported: () => Promise<void>;
}) {
  const [preview, setPreview] = useState<ApiCrmCustomerImportPreviewDto | null>(null);
  const [busy, setBusy] = useState(false);
  const [feedback, setFeedback] = useState<OperationFeedbackState | null>(null);

  async function selectFile(file?: File) {
    if (!canOperate || !file) return;
    setBusy(true);
    try {
      setPreview(await client.previewCrmCustomerImport({ fileName: file.name, body: file }));
      setFeedback(null);
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); setPreview(null); }
    finally { setBusy(false); }
  }

  async function confirmImport() {
    if (!canOperate || !preview?.validRows) return;
    setBusy(true);
    try {
      const result = await client.importCrmCustomers({ body: { rows: preview.rows } });
      setFeedback(successFeedback(`已导入 ${result.createdCustomers} 家客户、${result.createdContacts} 位联系人，跳过 ${result.skippedDuplicates} 行。`));
      setPreview(null);
      await onImported();
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); }
    finally { setBusy(false); }
  }

  return <section className="form-section">
    <div className="section-header"><h3>客户文件导入</h3><span>CSV / XLSX，最多 5000 行、10 MB</span></div>
    <div className="form-actions">
      <label className={`secondary-button${canOperate ? "" : " disabled"}`}>选择文件<input type="file" hidden disabled={!canOperate} accept=".csv,.xlsx,.xlsm" onChange={(event) => void selectFile(event.target.files?.[0])} /></label>
      <button className="primary-button" type="button" disabled={!canOperate || busy || !preview?.validRows} onClick={() => void confirmImport()}>{busy ? "处理中..." : "确认导入有效行"}</button>
    </div>
    <OperationFeedback feedback={feedback} />
    {preview ? <>
      <p>共 {preview.totalRows} 行，有效 {preview.validRows} 行，重复 {preview.duplicateRows} 行。</p>
      <ResponsiveTableFrame label="CRM 客户导入预览" mobileLayout="scroll"><table className="data-table"><thead><tr><th>行</th><th>客户</th><th>国家</th><th>联系人</th><th>结果</th></tr></thead>
        <tbody>{preview.rows.slice(0, 30).map((row) => <tr key={row.rowNumber}><td>{row.rowNumber}</td><td>{row.name || "-"}</td><td>{row.countryRegion || "-"}</td><td>{row.contactName || "-"}</td><td>{row.error || (row.isDuplicate ? "重复，跳过" : "可导入")}</td></tr>)}</tbody>
      </table></ResponsiveTableFrame>
    </> : null}
  </section>;
}
