import type { ApiRequestInit, ExportDocManagerApiClient, OaRequestSave, OaWorkflowCommandSave } from "../../api/index.ts";
import { acknowledgedCommands, officeCommand, type OfficeActionInput, type ApprovalReassignInput } from "../office/officeCommands.ts";
import type { OaActionName, OaKind } from "./oaModel.ts";

const names = { personnel: "PersonnelChange", leave: "Leave", overtime: "Overtime", expense: "Expense", travel: "Travel", purchase: "Purchase", general: "General" } as const;
// Only generated client methods own URLs, authentication, errors and cancellation.
export function oaApi(client: ExportDocManagerApiClient, kind: OaKind) {
  const name = names[kind];
  const execute = acknowledgedCommands(client, `${name}Request`);
  return {
    deleteDraft: (id: number, input: OfficeActionInput, init?: ApiRequestInit) => client[`delete${name}RequestDraft`]({ id, body: officeCommand(client,kind+":"+id+":delete-draft",input) }, init),
    list: (request: { pageNumber: number; pageSize: number; mineOnly: boolean; status?: string; financeOnly?: boolean; approvalsOnly?: boolean; handlingOnly?: boolean; definitionKey?:string }, init?: ApiRequestInit) => client[`list${name}Request`](request, init),
    get: (id: number, init?: ApiRequestInit) => client[`get${name}Request`]({ id }, init),
    previewApproval: (id: number, init?: ApiRequestInit) => client[`previewApprovalOf${name}Request`]({ id }, init),
    reassign: (id: number, body: ApprovalReassignInput, init?: ApiRequestInit) => execute(id,officeCommand(client,`${kind}:${id}:reassign`,body),init,(request,options)=>client[`reassign${name}RequestApproval`](request,options)),
    workflow: (id:number,body:OaWorkflowCommandSave,init?:ApiRequestInit)=>execute(id,body,init,(request,options)=>client[`execute${name}RequestWorkflow`](request,options)),
    fulfillment:(id:number,input:OfficeActionInput,init?:ApiRequestInit)=>execute(id,officeCommand(client,`${kind}:${id}:fulfillment`,input),init,(request,options)=>client[`execute${name}RequestFulfillment`](request,options)),
    workflowCandidates: (id:number,beforeId?:number,init?:ApiRequestInit)=>client[`list${name}RequestWorkflowCandidates`]({id,beforeId},init),
    create: (body: OaRequestSave, init?: ApiRequestInit) => client[`create${name}Request`]({ body }, init),
    update: (id: number, body: OaRequestSave, init?: ApiRequestInit) => client[`update${name}Request`]({ id, body }, init),
    action: (action: OaActionName, id: number, input: OfficeActionInput, init?: ApiRequestInit) => {
      const body=officeCommand(client,`${kind}:${id}:${action}`,input);
      return execute(id,body,init,(request,options) => {
        if (action === "return") return client[`returnForCorrection${name}Request`](request,options);
        if (action === "complete" || action === "void") {
          if (kind === "personnel") throw new Error("人员变动最终批准后自动生效；后续变更须另行申请。");
          return client[`${action}${names[kind]}Request`](request,options);
        }
        return client[`${action}${name}Request`](request,options);
      });
    },
    history: (id: number, pageNumber: number, init?: ApiRequestInit) => client[`listHistoryOf${name}Request`]({ id, pageNumber, pageSize: 20 }, init),
    decisions:(id:number,pageNumber:number,init?:ApiRequestInit)=>client[`listDecisionsOf${name}Request`]({id,pageNumber,pageSize:20},init),
    submissions: (id: number, pageNumber: number, init?: ApiRequestInit) => client[`listSubmissionsOf${name}Request`]({ id, pageNumber, pageSize: 20 }, init),
    submission: (id: number, submissionId: number, init?: ApiRequestInit) => client[`getSubmissionOf${name}Request`]({ id, submissionId }, init),
    approvalPdf: (id: number, submissionId: number, init?: ApiRequestInit) => client[`downloadApprovalOf${name}Request`]({ id, submissionId }, init),
    materials: (id: number, submissionId: number, init?: ApiRequestInit) => client[`downloadMaterialsOf${name}Request`]({ id, submissionId }, init),
    generateApproval: (id: number, submissionId: number, init?: ApiRequestInit) => client[`generateApprovalOf${name}Request`]({ id, submissionId }, init),
    upload: (id: number, body: FormData, init?: ApiRequestInit) => client[`uploadAttachmentTo${name}Request`]({ id, body }, init),
    download: (id: number, attachmentId: number, init?: ApiRequestInit) => client[`downloadAttachmentOf${name}Request`]({ id, attachmentId }, init),
    remove: (id: number, attachmentId: number, body: OfficeActionInput, init?: ApiRequestInit) => client[`deleteAttachmentOf${name}Request`]({ id, attachmentId, body:officeCommand(client,`${kind}:${id}:remove:${attachmentId}`,body) }, init),
  };
}
