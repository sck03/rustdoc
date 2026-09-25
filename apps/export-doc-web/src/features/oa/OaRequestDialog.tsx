import { useState, type FormEvent, type Dispatch, type SetStateAction } from "react";
import type { ApiUserDto, ExportDocManagerApiClient, OaRequest, OaRequestSave, PersonnelDirectoryRecord } from "../../api/index.ts";
import { businessDateTimeLocalInputToIso, toBusinessDateTimeLocalInput } from "../../ui/businessTime.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { OfficeEmployeePicker } from "../office/OfficeEmployeePicker.tsx";
import { OaLineEditor } from "./OaLineEditor.tsx";
import { OaTemporalFields } from "./OaTemporalFields.tsx";
import { oaApi } from "./oaApi.ts";
import { oaDraft, oaModules, type OaKind } from "./oaModel.ts";
import { useOfficeOperation } from "../office/useOfficeData.ts";

export function OaRequestDialog({ client, user, kind, record, onClose, onSaved }: { client: ExportDocManagerApiClient; user: ApiUserDto; kind: OaKind; record?: OaRequest; onClose: () => void; onSaved: (record: OaRequest) => void }) {
  const operation = useOfficeOperation();
  const [draft, storeDraft] = useState<OaRequestSave>(() => record ? { ...record, expectedVersion: record.versionNumber,
    ...(record.overtime ? { overtime: { ...record.overtime, startsAt: toBusinessDateTimeLocalInput(record.overtime.startsAt, user.businessTimeZone), endsAt: toBusinessDateTimeLocalInput(record.overtime.endsAt, user.businessTimeZone) } } : {}) } : oaDraft(kind, user));
  const [employee, setEmployee] = useState<PersonnelDirectoryRecord | null>(null);
  const [dirty, setDirty] = useState(false);
  const setDraft: Dispatch<SetStateAction<OaRequestSave>> = (value) => { setDirty(true); storeDraft(value); };
  useUnsavedChangesGuard({ isDirty: dirty, message: "申请有未保存的修改。" });
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void operation.run((signal) => {
      const body: OaRequestSave = { ...draft, employeeId: record?.employeeId ?? employee?.id };
      if (draft.overtime) {
        const startsAt = businessDateTimeLocalInputToIso(draft.overtime.startsAt, user.businessTimeZone);
        const endsAt = businessDateTimeLocalInputToIso(draft.overtime.endsAt, user.businessTimeZone);
        if (!startsAt || !endsAt) throw new Error("请填写完整加班时段。");
        body.overtime = { ...draft.overtime, startsAt, endsAt };
      }
      const api = oaApi(client, kind);
      return record ? api.update(record.id, body, { signal }) : api.create(body, { signal });
    }, (result) => { setDirty(false); onSaved(result); });
  }
  return <OfficeDialog title={`${record ? "编辑" : "新建"}${oaModules[kind].name}`} onClose={onClose} {...operation} protectChanges hasChanges={dirty}>
    <p className="office-muted">{oaModules[kind].description}先保存草稿，再上传附件和提交审批。</p>
    <form onSubmit={submit} onChangeCapture={() => setDirty(true)}><fieldset disabled={operation.busy} className="office-form-grid">
      {user.capabilities.usesOfficeRegister && !record && <OfficeEmployeePicker client={client} user={user} value={employee} onChange={(value) => { setEmployee(value); setDirty(true); }} disabled={operation.busy} />}
      {record && <p className="office-field-wide">申请人：{record.employeeName}</p>}
      <OfficeField label="申请标题" wide><input required maxLength={150} value={draft.title} onChange={(event) => setDraft({ ...draft, title: event.target.value })} /></OfficeField>
      <OfficeField label="申请说明" wide><textarea required rows={3} maxLength={2000} value={draft.reason} onChange={(event) => setDraft({ ...draft, reason: event.target.value })} /></OfficeField>
      <OaTemporalFields draft={draft} setDraft={setDraft} timeZone={user.businessTimeZone} />
      {(kind === "expense" || kind === "purchase") && <>
        <OfficeField label="币种"><select value={draft.currency} onChange={(event) => setDraft({ ...draft, currency: event.target.value as OaRequestSave["currency"] })}>{["CNY", "USD", "EUR", "HKD", "JPY", "GBP"].map((currency) => <option key={currency}>{currency}</option>)}</select></OfficeField>
        <OaLineEditor draft={draft} setDraft={setDraft} purchase={kind === "purchase"} businessDate={user.businessDate} />
      </>}
    </fieldset><OfficeSubmit busy={operation.busy} label="保存草稿" disabled={user.capabilities.usesOfficeRegister && !record && !employee} /></form>
  </OfficeDialog>;
}
