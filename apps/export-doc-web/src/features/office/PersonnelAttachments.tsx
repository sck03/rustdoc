import { useState } from "react";
import type { ExportDocManagerApiClient, PersonnelAttachment, PersonnelRecord } from "../../api/index.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { OfficeDialog, OfficeField, OfficeSubmit } from "./OfficeUi.tsx";
import { useOfficeOperation } from "./useOfficeData.ts";

export function PersonnelAttachments({ client, record }: { client: ExportDocManagerApiClient; record: PersonnelRecord }) {
  const operation = useOfficeOperation();
  const [removing, setRemoving] = useState<PersonnelAttachment | null>(null);
  return <section aria-label="档案文档"><h3>档案文档</h3>
    <p className="office-muted">编辑档案时可添加图片或 PDF 文档。文档仅对获授权的人事人员开放。</p>
    {operation.error && <InlineNotice tone="error">{operation.error}</InlineNotice>}
    {!record.attachments?.length && <p>尚无文档附件。</p>}
    <ul>{record.attachments?.map(file => <li key={file.id} className="office-card-actions"><span>{file.fileName} · {Math.ceil(file.sizeBytes / 1024)} KiB</span>
      <button type="button" className="command-button secondary" disabled={operation.busy} onClick={() => void operation.run(signal => client.downloadPersonnelAttachment({ id: record.employee.id, attachmentId: file.id }, { signal }), blob => downloadBlob(blob, file.fileName))}>下载</button>
      {record.canEdit && <button type="button" className="command-button secondary" disabled={operation.busy} onClick={() => setRemoving(file)}>移除</button>}
    </li>)}</ul>
    {removing && <OfficeDialog title="移除档案文档" onClose={() => setRemoving(null)} {...operation} protectChanges>
      <p>{removing.fileName}</p><form onSubmit={event => {
        event.preventDefault(); const note = String(new FormData(event.currentTarget).get("note") ?? "");
        void operation.run(signal => client.deletePersonnelAttachment({ id: record.employee.id, attachmentId: removing.id, body: { expectedVersion: record.versionNumber, note } }, { signal }), () => setRemoving(null));
      }}><OfficeField label="移除说明"><textarea name="note" required maxLength={500} disabled={operation.busy} /></OfficeField><OfficeSubmit busy={operation.busy} label="移除文档" /></form>
    </OfficeDialog>}
  </section>;
}
