import type { OaApprovalStage } from "../../api/index.ts";
export const workflowKinds:Record<NonNullable<OaApprovalStage["kind"]>,string>={Start:"开始",Approval:"审批／办理核验",Exclusive:"排他条件",Fork:"并行分叉",Join:"汇合",Copy:"抄送",Handling:"实际办理",System:"受控系统动作",Subflow:"子流程",End:"结束"};
