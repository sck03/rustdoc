import type { ApiRequestInit, ExportDocManagerApiClient, OaWorkflowCommandSave } from "../../api/index.ts";
import { acknowledgedCommands, officeCommand,type OfficeActionInput } from "./officeCommands.ts";
import type { OfficeKind, OfficeRequestRow } from "./officeModel.ts";

export function officeRequestApi(client: ExportDocManagerApiClient, kind: OfficeKind) {
  const name = kind === "rooms" ? "MeetingBooking" : "OfficeSupplyRequest";
  const execute = acknowledgedCommands(client, name);
  return {
    get: (id: number, init?: ApiRequestInit): Promise<OfficeRequestRow> => client[`get${name}`]({id},init),
    submit: (id: number, body: OfficeActionInput, init?: ApiRequestInit): Promise<OfficeRequestRow> => execute(id,officeCommand(client,`${kind}:${id}:submit`,body),init,(request,options)=>client[`submit${name}`](request,options)),
    workflow:(id:number,body:OaWorkflowCommandSave,init?:ApiRequestInit)=>execute(id,body,init,(request,options)=>client[`execute${name}Workflow`](request,options)),
    fulfillment:(id:number,input:OfficeActionInput,init?:ApiRequestInit)=>execute(id,officeCommand(client,`${kind}:${id}:fulfillment`,input),init,(request,options)=>client[`execute${name}Fulfillment`](request,options)),
    workflowCandidates:(id:number,beforeId?:number,init?:ApiRequestInit)=>client[`list${name}WorkflowCandidates`]({id,beforeId},init),
    upload: (id: number, body: FormData, init?: ApiRequestInit): Promise<OfficeRequestRow> => client[`uploadAttachmentTo${name}`]({id,body},init),
    download: (id: number, attachmentId: number, init?: ApiRequestInit) => client[`downloadAttachmentOf${name}`]({id,attachmentId},init),
    remove: (id: number, attachmentId: number, body: OfficeActionInput, init?: ApiRequestInit): Promise<OfficeRequestRow> => client[`deleteAttachmentOf${name}`]({id,attachmentId,body:officeCommand(client,`${kind}:${id}:remove:${attachmentId}`,body)},init),
    submission: (id: number, submissionId: number, init?: ApiRequestInit) => client[`getSubmissionOf${name}`]({id,submissionId},init),
    decisions:(id:number,pageNumber:number,init?:ApiRequestInit)=>client[`listDecisionsOf${name}`]({id,pageNumber,pageSize:20},init),
    generateApproval: (id: number, submissionId: number, init?: ApiRequestInit) => client[`generateApprovalOf${name}`]({id,submissionId},init),
    approvalPdf: (id: number, submissionId: number, init?: ApiRequestInit) => client[`downloadApprovalOf${name}`]({id,submissionId},init),
    materials: (id: number, submissionId: number, init?: ApiRequestInit) => client[`downloadMaterialsOf${name}`]({id,submissionId},init),
  };
}
