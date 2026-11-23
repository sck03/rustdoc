import { useState } from "react";
import type { ExportDocManagerApiClient, OaRequest } from "../../api/index.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { oaApi } from "./oaApi.ts";
import { useOaOperation } from "./useOaOperation.ts";

export function OaDraftDeleteDialog({ client, row, onClose, onDeleted }: {
  client: ExportDocManagerApiClient; row: OaRequest; onClose: () => void; onDeleted: () => void;
}) {
  const operation = useOaOperation(row.kind);
  const [note, setNote] = useState("");
  return <OfficeDialog title="删除草稿" onClose={onClose} {...operation}>
    <p>将删除“{row.title}”及草稿附件，操作无法撤销。</p>
    <form onSubmit={event => { event.preventDefault(); void operation.run(signal => oaApi(client,row.kind).deleteDraft(row.id,{expectedVersion:row.versionNumber,note}, {signal}),onDeleted); }}>
      <OfficeField label="删除原因"><input required maxLength={500} value={note} disabled={operation.busy} onChange={event => setNote(event.target.value)} /></OfficeField>
      <OfficeSubmit busy={operation.busy} label="确认删除草稿" />
    </form>
  </OfficeDialog>;
}
