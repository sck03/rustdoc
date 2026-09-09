import { useState } from "react";
import { Link } from "react-router-dom";
import type { ApiUserDto, ExportDocManagerApiClient, PersonnelDepartmentRecord, PersonnelImageKind, PersonnelRecord } from "../../api/index.ts";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { OfficeDialog, OfficePager, OfficeQueryState } from "./OfficeUi.tsx";
import { officeAccess, officeStatusLabels } from "./officeModel.ts";
import { employmentStatusLabels, employmentTypeLabels, personnelActionLabels, personnelHistoryLabels, personnelReminders, personnelWorkflows, type PersonnelWorkflow } from "./personnelModel.ts";
import { usePersonnelClearance, usePersonnelHistory, usePersonnelRecord } from "./usePersonnelData.ts";
import { PersonnelFormDialog } from "./PersonnelFormDialog.tsx";
import { PersonnelAccountDialog, PersonnelWorkflowDialog } from "./PersonnelWorkflowDialog.tsx";
import { PersonnelImagesPanel } from "./PersonnelImagesPanel.tsx";
import { useOfficeOperation } from "./useOfficeData.ts";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";

type Props = { client: ExportDocManagerApiClient; user: ApiUserDto; id: number; departments: PersonnelDepartmentRecord[]; onClose: () => void };
export function PersonnelDetailsDialog({ client, user, id, departments, onClose }: Props) {
  const query = usePersonnelRecord(client, user, id);
  const [tab, setTab] = useState("profile");
  const [pendingImages, setPendingImages] = useState(new Set<PersonnelImageKind>());
  const imageOperation = useOfficeOperation();
  const requestConfirmation = useConfirmation();
  async function changeTab(next: string) {
    if (next === tab || imageOperation.busy) return;
    if (pendingImages.size && !await requestConfirmation({ title: "放弃未上传的图片？", description: "当前选择的图片尚未上传保存。", confirmLabel: "放弃并切换" })) return;
    setPendingImages(new Set());
    setTab(next);
  }
  const [editing, setEditing] = useState<PersonnelRecord | null>(null);
  const [linking, setLinking] = useState<PersonnelRecord | null>(null);
  const [workflow, setWorkflow] = useState<{ record: PersonnelRecord; action: PersonnelWorkflow } | null>(null);
  const record = query.data;
  return <>
    <OfficeDialog title={record ? `${record.employee.fullName} · 人员档案` : "人员档案"} onClose={onClose}
      busy={imageOperation.busy} protectChanges={pendingImages.size > 0} hasChanges={pendingImages.size > 0}>
      {query.isPending ? <PageState tone="loading" title="正在读取人员档案" /> : query.isError ? <PageState tone="error" title="档案加载失败" description={readApiError(query.error)}
        action={<button type="button" onClick={() => void query.refetch()}>重新加载</button>} /> : record && <>
        <div className="office-card-heading"><span>{record.employee.employeeNumber} · {record.employee.departmentName} · {record.employee.jobTitle}</span>
          <span className="office-badge" data-state={record.employee.status}>{employmentStatusLabels[record.employee.status]}</span></div>
        <div className="office-card-actions">
          {record.canEdit && <button type="button" className="command-button" disabled={imageOperation.busy} onClick={() => setEditing(record)}>维护档案</button>}
          {personnelWorkflows(record).map((action) => <button type="button" className="command-button secondary" disabled={imageOperation.busy} key={action} onClick={() => setWorkflow({ record, action })}>{personnelActionLabels[action]}</button>)}
          {record.canLinkAccount && <button type="button" className="command-button secondary" disabled={imageOperation.busy} onClick={() => setLinking(record)}>关联账号</button>}
        </div>
        <nav className="office-tabs" aria-label="人员档案内容">{[["profile", "档案信息"], ["images", "照片与证件"], ["history", "任职与操作记录"], ["clearance", "交接事项"]].map(([key, label]) =>
          <button type="button" key={key} disabled={imageOperation.busy} aria-pressed={tab === key} onClick={() => void changeTab(key)}>{label}</button>)}</nav>
        {tab === "profile" && <PersonnelFacts record={record} user={user} />}
        {tab === "images" && <PersonnelImagesPanel client={client} record={record} operation={imageOperation}
          onPendingChange={(kind, pending) => setPendingImages((current) => { const next = new Set(current); if (pending) next.add(kind); else next.delete(kind); return next; })} />}
        {tab === "history" && <PersonnelHistory client={client} user={user} id={id} />}
        {tab === "clearance" && <PersonnelClearancePanel client={client} user={user} record={record} />}
      </>}
    </OfficeDialog>
    {editing && <PersonnelFormDialog client={client} user={user} departments={departments} record={editing} onClose={() => setEditing(null)} onSaved={() => setEditing(null)} />}
    {workflow && <PersonnelWorkflowDialog client={client} user={user} departments={departments} {...workflow} onClose={() => setWorkflow(null)} />}
    {linking && <PersonnelAccountDialog client={client} user={user} record={linking} onClose={() => setLinking(null)} />}
  </>;
}

function PersonnelFacts({ record, user }: { record: PersonnelRecord; user: ApiUserDto }) {
  const [showIdentity, setShowIdentity] = useState(false);
  const reminders = personnelReminders(record, user.businessDate);
  const facts = [
    ["用工类型", employmentTypeLabels[record.employmentType]], ["本次入职日期", record.hireDate],
    ["试用截止", record.probationEndsOn], ["合同截止", record.contractEndsOn], ["转正日期", record.confirmedOn], ["离职日期", record.departedOn],
    ["工作邮箱", record.profile.workEmail], ["工作电话", record.profile.workPhone], ["工作地点", record.profile.workLocation],
    ["个人电话", record.profile.personalPhone], ["紧急联系人", record.profile.emergencyContact], ["紧急联系电话", record.profile.emergencyPhone],
    ["签发机关", record.profile.identityAuthority], ["身份证住址", record.profile.registeredAddress],
    ["证件有效起始日", record.profile.identityValidFrom], ["证件有效截止日", record.profile.identityLongTerm ? "长期有效" : record.profile.identityValidUntil],
    ...(!user.capabilities.usesOfficeRegister ? [["关联账号", record.account ? `${record.account.username}（${record.account.isActive ? "启用" : "停用"}）` : "未关联"]] : []),
  ];
  return <div className="personnel-detail-stack">
    {reminders.length > 0 && <InlineNotice tone="warning" title="待关注">{reminders.join("；")}</InlineNotice>}
    <dl className="personnel-facts"><div className="personnel-wide"><dt>居民身份证号码</dt><dd>{record.profile.identityNumber ? <>
      <span>{showIdentity ? record.profile.identityNumber : `${record.profile.identityNumber.slice(0, 6)}********${record.profile.identityNumber.slice(-4)}`}</span>{" "}
      <button className="command-button secondary" type="button" aria-pressed={showIdentity} onClick={() => setShowIdentity((value) => !value)}>{showIdentity ? "隐藏号码" : "查看完整号码"}</button>
    </> : "未登记"}</dd></div>
      {facts.map(([label, value]) => <div key={label} className={["工作邮箱", "工作地点", "关联账号", "身份证住址"].includes(label ?? "") ? "personnel-wide" : undefined}><dt>{label}</dt><dd>{value || "未登记"}</dd></div>)}
      <div className="personnel-wide"><dt>人事备注</dt><dd>{record.profile.notes || "无"}</dd></div>
    </dl>
    <p className="office-muted">个人联系方式和人事记录仅对获授权的人员显示。</p>
  </div>;
}

function PersonnelHistory({ client, user, id }: { client: ExportDocManagerApiClient; user: ApiUserDto; id: number }) {
  const model = usePersonnelHistory(client, user, id);
  return <><OfficeQueryState query={model.query} emptyTitle="尚无操作记录" />
    {!model.query.isError && <ol className="office-history">{model.query.data?.items.map((item) => <li key={item.id}>
      <strong>{personnelHistoryLabels[item.action] ?? item.action} · 生效日期 {item.effectiveDate}</strong><p>{item.summary}</p>{item.note && <p>{item.note}</p>}
      <time dateTime={item.createdAt}>{formatBusinessDateTime(item.createdAt, user.businessTimeZone)} · {item.actorName}</time>
    </li>)}</ol>}
    <OfficePager page={model.query.data} paging={model.paging} busy={model.query.isFetching} />
  </>;
}

export function PersonnelClearancePanel({ client, user, record }: { client: ExportDocManagerApiClient; user: ApiUserDto; record: PersonnelRecord }) {
  const query = usePersonnelClearance(client, user, record.employee.id);
  if (query.isPending) return <PageState tone="loading" title="正在核对交接事项" />;
  if (query.isError) return <PageState tone="error" title="交接核对失败" description={readApiError(query.error)} action={<button type="button" onClick={() => void query.refetch()}>重新核对</button>} />;
  const data = query.data;
  return <div className="personnel-clearance">
    {data.managedDepartments.length > 0 && <InlineNotice tone="warning" title="离职前需调整部门负责人">
      {data.managedDepartments.map((department) => department.name).join("、")}仍由该人员负责。请由管理员调整负责人后再办理离职。
      {user.capabilities.canManageUsers && <> <Link to="/system/organization">前往组织架构</Link></>}
    </InlineNotice>}
    <InlineNotice tone={data.isClear ? "success" : "warning"} title={data.isClear ? "行政交接已结清" : "还有事项需要处理"}>
      {data.isClear ? "目前没有未结清的预约、钥匙或借用物品。" : `预约 ${data.meetingCount} 笔，物品申请／借用 ${data.supplyCount} 笔。请先完成归还，或取消未交接申请。`}
    </InlineNotice>
    {data.items.length > 0 && <ul>{data.items.map((item) => {
      const kind = item.kind === "rooms" ? "rooms" : "supplies";
      const path = kind === "rooms" ? "/office/meeting-rooms" : "/office/supplies";
      const label = `${item.resourceName} · #${item.requestId} · ${officeStatusLabels[item.status] ?? item.status}${item.outstandingQuantity ? ` · 待归还 ${item.outstandingQuantity}` : ""}`;
      return <li key={`${kind}-${item.requestId}`}>{officeAccess(user, kind).canSeeOthers ? <Link to={`${path}?requestId=${item.requestId}`}>{label}</Link> : label}</li>;
    })}</ul>}
    {(data.meetingCount > 20 || data.supplyCount > 20) && <p className="office-muted">每类显示前 20 笔，请在相应行政页面按申请人查看全部记录。</p>}
    <div className="office-card-actions"><button type="button" className="command-button secondary" disabled={query.isFetching} onClick={() => void query.refetch()}>重新核对</button>
      {(user.capabilities.usesOfficeRegister || record.account) && (["rooms", "supplies"] as const).filter((kind) => officeAccess(user, kind).canSeeOthers).map((kind) =>
        <Link key={kind} to={`/office/${kind === "rooms" ? "meeting-rooms" : "supplies"}?${user.capabilities.usesOfficeRegister ? `employeeId=${record.employee.id}` : `applicantUserId=${record.account!.id}`}`}>{kind === "rooms" ? "全部预约记录" : "全部物品记录"}</Link>)}
    </div>
  </div>;
}
