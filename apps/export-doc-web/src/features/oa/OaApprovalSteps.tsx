import type { ApiUserDto, OaApprovalPlan } from "../../api/index.ts";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
export function OaApprovalSteps({plan,notes,user}:{plan?:OaApprovalPlan;notes?:string[];user:ApiUserDto}){
  return <>
    {Boolean(notes?.length)&&<section className="oa-detail-content" aria-label="审批路由说明"><h3>审批路由说明</h3><ul>{notes!.map((n,i)=><li key={i}>{n}</li>)}</ul></section>}
    {plan&&<section className="oa-detail-content" aria-label="审批步骤"><h3>审批流程 · {plan.policyVersion === 0 ? "草稿模拟" : `版本 ${plan.policyVersion}`}</h3>
      <ol className="oa-history">{plan.nodes.map((node,index)=><li key={node.key}><strong>{node.name}</strong>
        {Boolean(node.reviewPoints?.length) && <div><span>审批要点</span><ul>{node.reviewPoints?.map((point,i)=><li key={i}>{point}</li>)}</ul></div>}
        {node.skipped?<p>已回避或条件不满足，跳过此节点。</p>:<><p>{node.startedAt?`开始：${formatBusinessDateTime(node.startedAt,user.businessTimeZone)}；期限：${formatBusinessDateTime(node.dueAt,user.businessTimeZone)}`:"等待前置节点完成"}</p>
          <ul>{plan.steps.filter(s=>s.stage===index).map(step=><li key={step.taskKey}>{step.approverName} · {{Waiting:"待审批",Approved:"已批准",Rejected:"不同意",Returned:"退回修改",Skipped:"无需处理（本节点已通过，本人未签署）",Cancelled:"已关闭"}[step.status]}
            {step.actedByUserId&&<p>{step.sourceGeneration?`沿用第 ${step.sourceGeneration} 次执行的签署 · `:""}{step.delegationKey?"代理办理：":"办理人："}{step.actedByName} · {formatBusinessDateTime(step.actedAt,user.businessTimeZone)} · {step.note}</p>}
          </li>)}</ul></>}
      </li>)}</ol>
      {plan.notifyUserIds.length>0&&<p>抄送账号：{plan.notifyUserIds.join("、")}</p>}
    </section>}
  </>;
}
