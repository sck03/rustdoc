import type { ApiRequestInit, ExportDocManagerApiClient, OaAction, OaRequestSave, OaApprovalReassign } from "../../api/index.ts";
import type { OaActionName, OaKind } from "./oaModel.ts";

const names = { personnel: "PersonnelChange", leave: "Leave", overtime: "Overtime", expense: "Expense", travel: "Travel", purchase: "Purchase", general: "General" } as const;
// Only generated client methods own URLs, authentication, errors and cancellation.
export function oaApi(client: ExportDocManagerApiClient, kind: OaKind) {
  const name = names[kind];
  return {
    list: (request: { pageNumber: number; pageSize: number; mineOnly: boolean; status?: string; financeOnly?: boolean; approvalsOnly?: boolean; handlingOnly?: boolean }, init?: ApiRequestInit) => client[`list${name}Request`](request, init),
    get: (id: number, init?: ApiRequestInit) => client[`get${name}Request`]({ id }, init),
    reassign: (id: number, body: OaApprovalReassign, init?: ApiRequestInit) => client[`reassign${name}RequestApproval`]({ id, body }, init),
    create: (body: OaRequestSave, init?: ApiRequestInit) => client[`create${name}Request`]({ body }, init),
    update: (id: number, body: OaRequestSave, init?: ApiRequestInit) => client[`update${name}Request`]({ id, body }, init),
    action: (action: OaActionName, id: number, body: OaAction, init?: ApiRequestInit) => {
      if (action === "complete" || action === "void") {
        if (kind === "personnel") throw new Error("人员变动最终批准后自动生效；后续变更须另行申请。");
        return client[`${action}${names[kind]}Request`]({ id, body }, init);
      }
      return client[`${action}${name}Request`]({ id, body }, init);
    },
    history: (id: number, pageNumber: number, init?: ApiRequestInit) => client[`listHistoryOf${name}Request`]({ id, pageNumber, pageSize: 20 }, init),
    upload: (id: number, body: FormData, init?: ApiRequestInit) => client[`uploadAttachmentTo${name}Request`]({ id, body }, init),
    download: (id: number, attachmentId: number, init?: ApiRequestInit) => client[`downloadAttachmentOf${name}Request`]({ id, attachmentId }, init),
    remove: (id: number, attachmentId: number, body: OaAction, init?: ApiRequestInit) => client[`deleteAttachmentOf${name}Request`]({ id, attachmentId, body }, init),
  };
}
