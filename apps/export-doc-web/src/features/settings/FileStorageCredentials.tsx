import { useLayoutEffect, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import type { ApiFileStorageStatus, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { Button } from "../../ui/Button.tsx";
import { FieldShell, TextField } from "../../ui/FormFields.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { CollapsibleSection } from "../../ui/primitives/Card.tsx";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { readApiError } from "../../ui/formUtils.ts";

export function FileStorageCredentials({ client, status, disabled }: {
  client: ExportDocManagerApiClient; status: ApiFileStorageStatus; disabled: boolean;
}) {
  const cache = useQueryClient();
  const [draft, setDraft] = useState({ accessKeyId: status.s3?.accessKeyId ?? "", secret: "", token: "" });
  const [version, setVersion] = useState(status.credentialVersion!);
  const [message, setMessage] = useState("");
  const [saved, setSaved] = useState(false);
  const discard = () => {
    setDraft({ accessKeyId: status.s3?.accessKeyId ?? "", secret: "", token: "" });
    setVersion(status.credentialVersion!);
    setMessage(""); setSaved(false);
  };
  useLayoutEffect(discard, [client]);
  useUnsavedChangesGuard({ isDirty: Boolean(draft.secret || draft.token || draft.accessKeyId !== status.s3?.accessKeyId), message: "存储凭据尚未保存。" });
  const save = useScopedMutation("file-storage-credentials", client, {
    mutationFn: (_: void, signal) => client.updateFileStorageCredentials({ body: { ...draft, expectedVersion: version } }, { signal }),
    onSuccess: response => {
      cache.setQueryData(queryKeys.fileStorage(), response);
      setVersion(response.credentialVersion!);
      setDraft(current => ({ ...current, secret: "", token: "" })); setMessage(""); setSaved(true);
    },
    onError: error => { setMessage(readApiError(error)); setSaved(false); },
  });
  return <CollapsibleSection title="更新存储凭据" error={Boolean(message)}>
    {message && <InlineNotice tone="error">{message}</InlineNotice>}
    {saved && <InlineNotice tone="success">凭据已更新。</InlineNotice>}
    <fieldset className="settings-fieldset" disabled={disabled || save.isPending}><div className="field-grid">
      <TextField label="Access Key ID" value={draft.accessKeyId} onChange={accessKeyId => setDraft(current => ({ ...current, accessKeyId }))} />
      {([["secret", "Secret Access Key"], ["token", "会话令牌（可选）"]] as const).map(([key, label]) =>
        <FieldShell key={key} label={label}>{descriptionId => <input type="password" aria-label={label} aria-describedby={descriptionId} autoComplete="new-password" value={draft[key]} onChange={event => setDraft(current => ({ ...current, [key]: event.target.value }))} />}</FieldShell>)}
    </div></fieldset>
    <div className="form-actions"><Button disabled={disabled || save.isPending || !draft.secret || !draft.accessKeyId} onClick={() => save.mutate()}>校验并更新凭据</Button><Button variant="secondary" disabled={disabled || save.isPending} onClick={discard}>放弃修改</Button></div>
  </CollapsibleSection>;
}
