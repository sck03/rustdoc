import type { ApiUserDto, OaSubmittedRequest, OaRequest } from "../../api/index.ts";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
import { personnelChangeLabels } from "./oaModel.ts";
import { expenseCategories } from "./OaLineEditor.tsx";
import { generalCategories, leaveCategories } from "./OaTemporalFields.tsx";
import { PersonnelOrganizationFacts } from "../office/PersonnelOrganizationEditor.tsx";

// Both current details and frozen submissions use the same read-only content.
export function OaRequestContent({row,user}:{row:OaSubmittedRequest|OaRequest;user:ApiUserDto}) {
  return <section className="oa-detail-content" aria-label="申请内容"><h3>申请内容</h3>
    <div className="oa-reason"><h4>申请说明</h4><p>{row.reason ?? "申请说明未在当前材料权限内展示。"}</p></div>
    {row.personnelChange && <dl className="oa-facts">
      <div><dt>变动类型</dt><dd>{row.personnelChange.action ? personnelChangeLabels[row.personnelChange.action] : "受限字段"}</dd></div>
      <div><dt>生效日期</dt><dd>{row.personnelChange.registration?.hireDate ?? row.personnelChange.effectiveDate}</dd></div>
      {(row.personnelChange.registration || ["Transfer", "Rehire"].includes(row.personnelChange.action ?? "")) && <>
        <div><dt>任职部门</dt><dd>{row.personnelChange.registration?.departmentId ?? row.personnelChange.departmentId}</dd></div>
        <div><dt>职位</dt><dd>{row.personnelChange.registration?.jobTitle ?? row.personnelChange.jobTitle}</dd></div>
      </>}
      <div><dt>提交账号</dt><dd>#{row.ownerUserId}（见审批与办理记录）</dd></div>
    </dl>}
    {row.leave && <p>{row.leave.category ? leaveCategories[row.leave.category] : "受限字段"} · {row.leave.startsOn} {row.leave.startPeriod === "AM" ? "上午" : "下午"} 至 {row.leave.endsOn} {row.leave.endPeriod === "AM" ? "上午" : "下午"} · {row.durationDays} 个自然日</p>}
    <PersonnelOrganizationFacts value={row.personnelChange?.organization} />
    {row.travel && <p>{row.travel.destination} · {row.travel.startsOn} 至 {row.travel.endsOn} · {row.durationDays} 天</p>}
    {row.overtime && <p>{row.overtime.location} · {formatBusinessDateTime(row.overtime.startsAt, user.businessTimeZone)} 至 {formatBusinessDateTime(row.overtime.endsAt, user.businessTimeZone)} · {row.durationHours} 小时</p>}
    {row.category && <p>申请类别：{generalCategories[row.category]}</p>}
    {row.handlingName && <p>办理事项：{row.handlingName} · 当前办理人员：{row.handlerNames || "请联系管理员核对分工"}</p>}
    {row.totalAmount && <p><strong>{row.kind === "purchase" ? "预算合计" : "报销合计"}：{row.currency} {row.totalAmount}</strong></p>}
    {row.lines && <ol className="oa-lines">{row.lines.map((line, index) => <li key={`${row.id}-${index}`} className="oa-line">{line.category ? expenseCategories[line.category] : "受限字段"} · {line.spentOn} · {line.description} · {line.amount} {row.currency}</li>)}</ol>}
    {row.purchaseLines && <ol className="oa-lines">{row.purchaseLines.map((line, index) => <li key={`${row.id}-${index}`} className="oa-line">{line.name} {line.specification} · {line.quantity} {line.unit} × {line.unitPrice} {row.currency}</li>)}</ol>}
    </section>;
}
