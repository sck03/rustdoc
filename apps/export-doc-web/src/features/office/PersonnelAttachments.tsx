import { useState } from "react";
import type { ExportDocManagerApiClient, PersonnelAttachment, PersonnelRecord } from "../../api/index.ts";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";
import { Button } from "../../ui/Button.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "./OfficeUi.tsx";
import { usePersonnelOperation, usePersonnelAttachments } from "./usePersonnelData.ts";
import { AttachmentUploadPanel } from "../../ui/AttachmentUploadPanel.tsx";

export function PersonnelAttachments({ client, record }: { client: ExportDocManagerApiClient; record: PersonnelRecord }) {
  const operation = usePersonnelOperation();
  const files = usePersonnelAttachments(client, record.employee.id, record.versionNumber);
  const [removing, setRemoving] = useState<PersonnelAttachment | null>(null);
  return <section aria-label="档案文档"><h3>档案文档（{files.query.data?.totalCount ?? record.attachmentCount ?? 0}）</h3>
    {record.canEdit && <AttachmentUploadPanel key={record.employee.id} record={record} operation={operation} label="上传档案文档" limitPerRecord={false} upload={(current, body, signal) => client.uploadPersonnelAttachment({ id: current.employee.id, body }, { signal })} />}
    {operation.error && <InlineNotice tone="error">{operation.error}</InlineNotice>}
    {files.query.isPending && <PageState tone="loading" title="正在读取档案文档" />}
    {files.query.isError && <InlineNotice tone="error" action={<Button variant="secondary" onClick={() => void files.query.refetch()}>重试</Button>}>{readApiError(files.query.error)}</InlineNotice>}
    <ul>{files.query.data?.items.map(file => <li key={file.id} className="office-card-actions"><span>{file.fileName} · {Math.ceil(file.sizeBytes / 1024)} KiB</span>
      <button type="button" className="command-button secondary" disabled={operation.busy} onClick={() => void operation.run(signal => client.downloadPersonnelAttachment({ id: record.employee.id, attachmentId: file.id }, { signal }), blob => downloadBlob(blob, file.fileName))}>下载</button>
      {record.canEdit && <button type="button" className="command-button secondary" disabled={operation.busy} onClick={() => setRemoving(file)}>移除</button>}
    </li>)}</ul>
    {(files.canPrevious || files.query.data?.nextCursor) && <div className="form-actions"><Button variant="secondary" disabled={!files.canPrevious || files.query.isFetching} onClick={files.previous}>上一页</Button><Button variant="secondary" disabled={!files.query.data?.nextCursor || files.query.isFetching} onClick={files.next}>下一页</Button></div>}
    {removing && <OfficeDialog title="移除档案文档" onClose={() => setRemoving(null)} {...operation} protectChanges>
      <p>{removing.fileName}</p><form onSubmit={event => {
        event.preventDefault(); const note = String(new FormData(event.currentTarget).get("note") ?? "");
        void operation.run(signal => client.deletePersonnelAttachment({ id: record.employee.id, attachmentId: removing.id, body: { expectedVersion: record.versionNumber, note } }, { signal }), () => setRemoving(null));
      }}><OfficeField label="移除说明"><textarea name="note" required maxLength={500} disabled={operation.busy} /></OfficeField><OfficeSubmit busy={operation.busy} label="移除文档" /></form>
    </OfficeDialog>}
  </section>;
}
