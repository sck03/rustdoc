import { useState } from "react";
import type { ExportDocManagerApiClient, PersonnelAttachment, PersonnelRecord } from "../../api/index.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { OfficeDialog, OfficeField, OfficeSubmit } from "./OfficeUi.tsx";
import { usePersonnelOperation } from "./usePersonnelData.ts";
import { AttachmentUploadPanel } from "../../ui/AttachmentUploadPanel.tsx";

export function PersonnelAttachments({ client, record }: { client: ExportDocManagerApiClient; record: PersonnelRecord }) {
  const operation = usePersonnelOperation();
  const [removing, setRemoving] = useState<PersonnelAttachment | null>(null);
  return <section aria-label="档案文档"><h3>档案文档（{record.attachments?.length ?? 0}）</h3>
    {record.canEdit && <AttachmentUploadPanel key={record.employee.id} record={record} operation={operation} label="上传档案文档" upload={(current, body, signal) => client.uploadPersonnelAttachment({ id: current.employee.id, body }, { signal })} />}
    {operation.error && <InlineNotice tone="error">{operation.error}</InlineNotice>}
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
