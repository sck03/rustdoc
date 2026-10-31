import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { downloadBlob } from "../../ui/downloadBlob.ts";
import { AttachmentUploadPanel } from "../../ui/AttachmentUploadPanel.tsx";
import { AttachmentDraftFields } from "../../ui/AttachmentDraftFields.tsx";
import { useAttachmentDraft } from "../../ui/useAttachmentDraft.ts";
import { attachmentUploadForm } from "../../ui/documentAttachments.ts";
import { toBusinessDateTimeLocalInput } from "../../ui/businessTime.ts";
import { ApprovalDocument } from "../oa/ApprovalDocument.tsx";
import { ApprovalReassignDialog } from "../oa/ApprovalReassignDialog.tsx";
import { WorkflowOperations } from "../oa/WorkflowOperations.tsx";
import { OfficeDialog, OfficeField, OfficeSubmit } from "./OfficeUi.tsx";
import { useOfficeResourceOperation } from "./useOfficeData.ts";
import { isMeetingBooking, officeAccess, readMeetingBookingUpdate, readSupplyRequestUpdate, type OfficeKind, type OfficeRequestRow } from "./officeModel.ts";
import { officeRequestApi } from "./officeRequestApi.ts";

export function ResourceRequestDetails(props: {client: ExportDocManagerApiClient;user:ApiUserDto;kind:OfficeKind;row:OfficeRequestRow}) {
  const [opened,setOpened] = useState(false);
  return <details onToggle={event=>setOpened(event.currentTarget.open)}><summary>申请附件、审批单与任务移交</summary>{opened && <Details {...props} />}</details>;
}
function Details({client,user,kind,row}: {client: ExportDocManagerApiClient;user:ApiUserDto;kind:OfficeKind;row:OfficeRequestRow}) {
  const api = officeRequestApi(client,kind);
  const operation = useOfficeResourceOperation(kind);
  const [editing,setEditing] = useState(false);
  const [reassigning,setReassigning] = useState(false);
  const [removing,setRemoving] = useState<number|null>(null);
  const [note,setNote] = useState("");
  const query = useQuery({queryKey:["office","resource-request",kind,user.id,user.contextId,row.id],queryFn:({signal})=>api.get(row.id,{signal})});
  if (query.isPending) return <p>正在读取申请资料…</p>;
  if (query.isError) return <InlineNotice tone="error">{readApiError(query.error)} <button type="button" onClick={()=>void query.refetch()}>重试</button></InlineNotice>;
  const current = query.data;
  const editable = current.status === "Draft" && current.ownerUserId === user.id && officeAccess(user,kind).allows("create",current);
  return <section aria-label="资源申请资料">
    {editable && <div className="office-card-actions"><button type="button" className="command-button secondary" onClick={()=>setEditing(true)}>继续编辑草稿</button><button type="button" className="command-button" disabled={operation.busy} onClick={()=>void operation.run(signal=>api.submit(current.id,{expectedVersion:current.versionNumber,note:""},{signal}),()=>{})}>提交申请</button></div>}
    <p className="office-muted">附件随草稿保存，提交后冻结原件。修改已提交资料须取消后重新申请。</p>
    {"reservationState" in current && current.reservationState === "Insufficient" && <InlineNotice tone="warning" title="批准已保留，库存待协调">当前尚未预留库存。补充库存后，办理人可重新尝试发放，系统会再次核对数量。</InlineNotice>}
    {editable && <AttachmentUploadPanel record={current} operation={operation} upload={(record,body,signal)=>{body.set("purpose","Supporting");return api.upload(record.id,body,{signal});}} />}
    <ul>{(current.attachments ?? []).map(file=><li className="office-card-actions" key={file.id}><span>{file.fileName} · {Math.ceil(file.sizeBytes/1024)} KiB</span><button type="button" className="command-button secondary" disabled={operation.busy} onClick={()=>void operation.run(signal=>api.download(current.id,file.id,{signal}),blob=>downloadBlob(blob,file.fileName),false)}>下载附件</button>{editable && <button type="button" className="text-button" onClick={()=>{setRemoving(file.id);setNote("");}}>移除附件</button>}</li>)}</ul>
    <ApprovalDocument api={api} user={user} row={current} scope={kind} />
    <WorkflowOperations client={client} row={current} disabled={query.isFetching}/>
    {(user.capabilities.canManageUsers || user.capabilities.unitManagement?.workflows) && current.status === "Pending" && <button type="button" className="command-button secondary" onClick={()=>setReassigning(true)}>移交未完成审批</button>}
    {operation.error && <InlineNotice tone="error">{operation.error}</InlineNotice>}
    {editing && <ResourceDraftEditor client={client} user={user} kind={kind} row={current} onClose={()=>setEditing(false)} />}
    {reassigning && <ApprovalReassignDialog client={client} row={current} onClose={()=>setReassigning(false)} />}
    {removing !== null && <OfficeDialog title="移除申请附件" onClose={()=>setRemoving(null)} {...operation} protectChanges hasChanges={note !== ""}><form onSubmit={event=>{event.preventDefault();void operation.run(signal=>api.remove(current.id,removing,{expectedVersion:current.versionNumber,note},{signal}),()=>setRemoving(null));}}><OfficeField label="移除原因"><textarea required maxLength={500} value={note} onChange={event=>setNote(event.target.value)} /></OfficeField><OfficeSubmit busy={operation.busy} label="确认移除附件" /></form></OfficeDialog>}
  </section>;
}
function ResourceDraftEditor({client,user,kind,row,onClose}: {client:ExportDocManagerApiClient;user:ApiUserDto;kind:OfficeKind;row:OfficeRequestRow;onClose:()=>void}) {
  const operation = useOfficeResourceOperation(kind);
  const attachments = useAttachmentDraft<OfficeRequestRow>(row);
  const api = officeRequestApi(client,kind);
  const [dirty,setDirty] = useState(false);
  return <OfficeDialog title="编辑资源申请草稿" onClose={onClose} {...operation} protectChanges hasChanges={dirty}>
    <form onChangeCapture={()=>setDirty(true)} onSubmit={event=>{event.preventDefault();const form=new FormData(event.currentTarget);const body=isMeetingBooking(row)?readMeetingBookingUpdate(form,row.versionNumber,user.businessTimeZone):readSupplyRequestUpdate(form,row.versionNumber);
      void operation.run(signal=>attachments.save(JSON.stringify(body),signal,(current,signal)=>"startsAt" in body ? client.updateDraftOfMeetingBooking({id:row.id,body:{...body,expectedVersion:current?.versionNumber ?? row.versionNumber}},{signal}):client.updateDraftOfOfficeSupplyRequest({id:row.id,body:{...body,expectedVersion:current?.versionNumber ?? row.versionNumber}},{signal}),
        (current,entry,signal)=>{const form=attachmentUploadForm(entry.file,current.versionNumber);form.set("purpose","Supporting");return api.upload(current.id,form,{signal});}),()=>{setDirty(false);onClose();});}}>
      <fieldset disabled={operation.busy} className="office-form-grid">{isMeetingBooking(row) ? <>
        <OfficeField label="会议主题" wide><input name="title" required maxLength={200} defaultValue={row.title}/></OfficeField><OfficeField label="参会人数"><input name="attendeeCount" required type="number" min={1} max={10000} defaultValue={row.attendeeCount}/></OfficeField>
        <OfficeField label="开始时间"><input name="startsAt" required type="datetime-local" defaultValue={toBusinessDateTimeLocalInput(row.startsAt,user.businessTimeZone)}/></OfficeField><OfficeField label="结束时间"><input name="endsAt" required type="datetime-local" defaultValue={toBusinessDateTimeLocalInput(row.endsAt,user.businessTimeZone)}/></OfficeField>
      </> : <><OfficeField label={`申请数量（${row.unit}）`}><input name="quantity" required type="number" min={1} max={1000000} defaultValue={row.quantity}/></OfficeField>{row.isReturnable && <OfficeField label="预计归还日期"><input name="returnDueDate" required type="date" min={user.businessDate} defaultValue={row.returnDueDate ?? user.businessDate}/></OfficeField>}<OfficeField label="领用用途" wide><textarea name="purpose" required maxLength={500} defaultValue={row.purpose}/></OfficeField></>}
        <AttachmentDraftFields draft={attachments} busy={operation.busy} existingFiles={attachments.saved?.attachments}/>
      </fieldset><OfficeSubmit busy={operation.busy} label="保存草稿"/>
    </form>
  </OfficeDialog>;
}
