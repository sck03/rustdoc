import { useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Mail, MapPin, Phone, Plus, RefreshCw, Search } from "lucide-react";
import type { ApiUserDto, ExportDocManagerApiClient, PersonnelDirectoryRecord } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { OfficePager, OfficeQueryState } from "./OfficeUi.tsx";
import { canViewPersonnelDetails, employmentStatusLabels } from "./personnelModel.ts";
import { usePersonnelDirectory } from "./usePersonnelData.ts";
import { PersonnelFormDialog } from "./PersonnelFormDialog.tsx";
import { PersonnelDetailsDialog } from "./PersonnelDetailsDialog.tsx";
import { PersonnelAvatar } from "./PersonnelAvatar.tsx";
import { departmentOptions } from "../organization/organizationModel.ts";
import "../../styles/routes/office.css";
import "../../styles/routes/personnel.css";

export function PersonnelPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const model = usePersonnelDirectory(client, user);
  const [creating, setCreating] = useState(false);
  const [search, setSearch] = useSearchParams();
  const focusedId = /^[1-9]\d*$/.test(search.get("employeeId") ?? "") ? Number(search.get("employeeId")) : null;
  const [selectedId, setSelectedId] = useState<number | null>(focusedId);
  const canViewDetails = canViewPersonnelDetails(user);
  return <section className="work-surface office-workspace personnel-workspace" aria-label="人员信息管理">
    <div className="personnel-heading"><div><h2>{user.capabilities.usesOfficeRegister ? "人员档案" : "公司通讯录"}</h2>
      <p className="office-muted">{user.capabilities.usesOfficeRegister ? "维护人员档案，再为人员登记预约和领用；入转离记录统一留存。" : "按部门查找同事，快速联系协作。"}</p></div>
      <div className="office-card-actions">{user.capabilities.canManageUsers && <Link className="command-button secondary" to="/system/organization">组织架构</Link>}
        {model.options.data?.canCreate && <button type="button" className="command-button" onClick={() => setCreating(true)}><Plus size={17} aria-hidden="true" />入职登记</button>}</div>
    </div>
    <div className="office-toolbar">
      <form className="office-search" onSubmit={(event) => { event.preventDefault(); model.search(); }}>
        <input aria-label="搜索人员" placeholder="姓名、工号、岗位或工作邮箱" value={model.keyword} maxLength={100} onChange={(event) => model.changeKeyword(event.target.value)} />
        <button type="submit" className="command-button secondary"><Search size={16} aria-hidden="true" />搜索</button>
      </form>
      <label className="office-filter">部门<select value={model.departmentId} onChange={(event) => model.changeDepartment(event.target.value)}>
        <option value="">全部部门</option>{departmentOptions(model.options.data?.departments ?? []).map((item) => <option key={item.code} value={item.code}>{item.label}{item.isActive ? "" : "（停用）"}</option>)}
      </select></label>
      <label className="office-filter">任职状态<select value={model.status} onChange={(event) => model.changeStatus(event.target.value)}>
        <option value="">全部在职</option><option value="Probation">试用期</option><option value="Active">正式在职</option>
        {canViewDetails && <option value="Departed">已离职</option>}
      </select></label>
      <button type="button" className="icon-button" aria-label="刷新通讯录" disabled={model.query.isFetching} onClick={() => void model.query.refetch()}><RefreshCw size={17} aria-hidden="true" /></button>
    </div>
    {canViewDetails && <label className="checkbox-field personnel-attention"><input type="checkbox" checked={model.attentionOnly} onChange={(event) => model.changeAttention(event.target.checked)} />仅看试用／合同／身份证已到期或 30 天内到期的人员</label>}
    {model.options.isError && <InlineNotice tone="error" title="部门目录加载失败">{readApiError(model.options.error)} <button type="button" onClick={() => void model.options.refetch()}>重新加载</button></InlineNotice>}
    <OfficeQueryState query={model.query} emptyTitle={model.keyword || model.departmentId || model.status || model.attentionOnly ? "没有符合条件的人员" : "尚未登记人员档案"} />
    {!model.query.isError && <div className="personnel-directory">{model.query.data?.items.map((employee) =>
      <PersonnelCard key={employee.id} client={client} employee={employee} onOpen={() => setSelectedId(employee.id)} />)}</div>}
    <OfficePager page={model.query.data} paging={model.paging} busy={model.query.isFetching} />
    {creating && <PersonnelFormDialog client={client} user={user} departments={model.options.data?.departments ?? []} onClose={() => setCreating(false)}
      onSaved={(record) => { setCreating(false); setSelectedId(record.employee.id); }} />}
    {selectedId !== null && canViewDetails && <PersonnelDetailsDialog client={client} user={user} id={selectedId} departments={model.options.data?.departments ?? []} onClose={() => { setSelectedId(null); if (focusedId) setSearch({}); }} />}
  </section>;
}

function PersonnelCard({ client, employee, onOpen }: { client: ExportDocManagerApiClient; employee: PersonnelDirectoryRecord; onOpen: () => void }) {
  return <article className="personnel-card">
    <header><PersonnelAvatar client={client} employee={employee} /><div>
      <h3>{employee.fullName}</h3><p className="office-muted">{employee.employeeNumber}</p></div>
      <span className="office-badge" data-state={employee.status}>{employmentStatusLabels[employee.status]}</span>
    </header>
    <p><strong>{employee.departmentName}</strong> · {employee.jobTitle}</p>
    <div className="personnel-contact"><Mail size={15} aria-hidden="true" />{employee.workEmail ? <a href={`mailto:${employee.workEmail}`}>{employee.workEmail}</a> : <span className="office-muted">未登记工作邮箱</span>}</div>
    <div className="personnel-contact"><Phone size={15} aria-hidden="true" /><span>{employee.workPhone || "未登记工作电话"}</span></div>
    {employee.workLocation && <div className="personnel-contact"><MapPin size={15} aria-hidden="true" /><span>{employee.workLocation}</span></div>}
    {employee.canViewDetails && <button type="button" className="command-button secondary" onClick={onOpen} aria-label={`查看${employee.fullName}的人员档案`}>人员档案</button>}
  </article>;
}
