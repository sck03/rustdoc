import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiPermissionTemplateDto, ExportDocManagerApiClient, PermissionBindingTarget, PermissionSchemeApply } from "../../api/index.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
import { useOrganizationOperation } from "../organization/useOrganizationDirectory.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
type Target = PermissionBindingTarget & { label: string };
export function PermissionPublicationActions({ client, scheme, disabled, unitTargets, accountTargets = false, onPublished, onBusy }: { client: ExportDocManagerApiClient; scheme: ApiPermissionTemplateDto; disabled: boolean; unitTargets?: Target[]; accountTargets?: boolean; onPublished: (versionNumber: number) => void; onBusy?: (busy: boolean) => void }) {
  const [opened, setOpened] = useState(false);
  const [note, setNote] = useState("");
  const [selected, setSelected] = useState<string[]>([]);
  const [version, setVersion] = useState<number | null>(scheme.publishedVersion ?? null);
  const [message, setMessage] = useState("");
  const operation = useOrganizationOperation();
  const command = useRef<{ payload: string; key: string } | null>(null);
  const key = (body: unknown) => { const payload = JSON.stringify(body); if (command.current?.payload !== payload) command.current = { payload, key: createRequestKey() }; return command.current.key; };
  useEffect(() => { onBusy?.(operation.busy); return()=>onBusy?.(false); }, [operation.busy, onBusy]);
  const versions = useQuery({ queryKey: ["permission-versions", scheme.id], enabled: opened, queryFn: ({ signal }) => client.getPermissionSchemeVersions({ schemeId: String(scheme.id), pageSize: 20 }, { signal }) });
  const users = useQuery({ queryKey: queryKeys.users(), enabled: opened && accountTargets, queryFn: ({ signal }) => client.listUsers({ signal }) });
  const targets: Target[] = unitTargets ?? (users.data?.users ?? []).filter(user => user.role !== "Admin" && user.permissionTemplateId === scheme.id && user.permissionGrants == null).map(user => ({ kind: "Account", id: String(user.id), expectedVersion: user.versionNumber ?? 1, label: `${user.fullName} · ${user.username}` }));
  const applyBody = (): PermissionSchemeApply => { const body = { schemeId: String(scheme.id), version: version ?? 0, targets: targets.filter(target => selected.includes(`${target.kind}:${target.id}`)).map(({ kind, id, expectedVersion }) => ({ kind, id, expectedVersion })), note }; return { ...body, commandKey: key(body) }; };
  return <details onToggle={event => setOpened(event.currentTarget.open)}><summary>发布版本与权限应用</summary><p>先保存草稿，再发布。现有身份继续使用原版本，应用时重新核验权限上限和在途职责。</p><OfficeField label="发布或应用原因"><input required maxLength={500} value={note} disabled={operation.busy} onChange={event => setNote(event.target.value)} /></OfficeField>
    <button type="button" className="command-button" disabled={disabled || operation.busy || !note.trim()} onClick={() => { const body = { schemeId: String(scheme.id), expectedVersion: scheme.versionNumber ?? 1, note }; void operation.run(signal => client.publishPermissionScheme({ body: { ...body, commandKey: key(body) } }, { signal }), result => { command.current = null; setVersion(result.version); setMessage(`已发布版本 ${result.version}`); onPublished(result.versionNumber); void versions.refetch(); }); }}>发布当前草稿</button>
    {versions.isError && <p role="alert">{readApiError(versions.error)}</p>}<OfficeField label="待应用发布版本"><select value={version ?? ""} disabled={operation.busy || !versions.data} onChange={event => setVersion(Number(event.target.value) || null)}><option value="">选择发布版本</option>{versions.data?.items.map(row => <option value={row.version} key={row.version}>版本 {row.version}</option>)}</select></OfficeField>
    <fieldset disabled={operation.busy}><legend>明确选择已绑定身份</legend>{targets.length ? targets.map(target => <label className="checkbox-field" key={`${target.kind}:${target.id}`}><input type="checkbox" checked={selected.includes(`${target.kind}:${target.id}`)} onChange={event => { const value = `${target.kind}:${target.id}`; setSelected(current => event.target.checked ? [...current, value] : current.filter(key => key !== value)); }} />{target.label}</label>) : <p>尚无可应用的已绑定身份。新身份在账号或单位授权中选择发布版本。</p>}</fieldset>
    <div className="office-card-actions"><button type="button" disabled={operation.busy || !version || !selected.length || !note.trim()} onClick={() => void operation.run(signal => client.previewPermissionSchemeApplication({ body: applyBody() }, { signal }), result => setMessage(`已核验 ${result.targets.length} 个身份；预览未改变权限。`))}>预览所选身份影响</button><button type="button" disabled={operation.busy || !version || !selected.length || !note.trim()} onClick={() => void operation.run(signal => client.applyPermissionSchemeVersion({ body: applyBody() }, { signal }), result => { command.current = null; setMessage(`已应用到 ${result.targets.length} 个身份。`); setSelected([]); })}>应用所选发布版本</button></div>
    {operation.error && <p role="alert">{operation.error}</p>}{message && <p role="status">{message}</p>}
  </details>;
}
