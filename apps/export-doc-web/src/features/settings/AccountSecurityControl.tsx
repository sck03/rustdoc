import { useState } from "react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { useOrganizationOperation } from "../organization/useOrganizationDirectory.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";
export function AccountSecurityControl({ client, id, version, name, onSaved }: { client: ExportDocManagerApiClient; id: number; version: number; name: string; onSaved: () => void }) {
  const [opened, setOpened] = useState(false);
  return <><button type="button" className="text-button" onClick={() => setOpened(true)}>紧急安全停用</button>{opened && <SecurityDialog client={client} id={id} version={version} name={name} onClose={() => setOpened(false)} onSaved={() => { setOpened(false); onSaved(); }} />}</>;
}
function SecurityDialog({ client, id, version, name, onClose, onSaved }: { client: ExportDocManagerApiClient; id: number; version: number; name: string; onClose: () => void; onSaved: () => void }) {
  const operation = useOrganizationOperation();
  const [note, setNote] = useState("");
  const [key] = useState(createRequestKey);
  return <OfficeDialog title="紧急安全停用账号" onClose={onClose} {...operation} protectChanges hasChanges={note !== ""}><p>{name} 将立即失去登录和业务权限。未结事项与历史继续保留并等待交接；重新启用时须重新明确授权。</p><form onSubmit={event => { event.preventDefault(); void operation.run(signal => client.suspendAccountForSecurity({ body: { accountId: String(id), expectedVersion: version, note, commandKey: key } }, { signal }), onSaved); }}><OfficeField label="安全停用原因"><textarea required maxLength={500} value={note} disabled={operation.busy} onChange={event => setNote(event.target.value)} /></OfficeField><OfficeSubmit busy={operation.busy} label="确认立即停用" /></form></OfficeDialog>;
}
