import type { ApiUserDto, OaApprovalPlan } from "../../api/index.ts";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";

export function OaApprovalSteps({ plan, notes, user }: { plan?: OaApprovalPlan; notes?: string[]; user: ApiUserDto }) {
  return <>
    {Boolean(notes?.length) && <section className="oa-detail-content" aria-label="审批路由说明"><h3>审批路由说明</h3><ul>{notes!.map((note, index) => <li key={index}>{note}</li>)}</ul></section>}
    {plan?.matchedBranch && <p>命中金额条件：{plan.matchedBranch}</p>}
    {plan?.mode === "Single" && <p>单步审批：由具有本申请审批权限的人员办理。</p>}
    {plan && plan.steps.length > 0 && <section className="oa-detail-content" aria-label="审批步骤"><h3>审批步骤</h3><ol className="oa-history">{plan.steps.map((step, index) => <li key={step.approverUserId}>
      <strong>第 {(step.stage ?? index) + 1} 阶段 · {step.decision === "Any" ? "或签" : "会签"} · {step.approverName}（账号 #{step.approverUserId}）</strong> · {{ Waiting: "待审批", Approved: "已批准", Rejected: "已驳回", Skipped: "本节点其他人已批准，未签署" }[step.status]}
      {step.actedByUserId && <p>{step.delegationKey ? "代理办理：" : "办理人："}{step.actedByName}（账号 #{step.actedByUserId}） · {formatBusinessDateTime(step.actedAt, user.businessTimeZone)} · {step.note}</p>}
    </li>)}</ol></section>}
  </>;
}
