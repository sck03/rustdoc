import { useState } from "react";
import { Link } from "react-router-dom";
import { Building2, Plus, RefreshCw, Search } from "lucide-react";
import type { ApiOrganizationCompanyDto, ApiOrganizationDepartmentDto, ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { useOrganizationDirectory, useOrganizationOperation } from "./useOrganizationDirectory.ts";
import { OrganizationCompanyForm, OrganizationDepartmentForm } from "./OrganizationForms.tsx";
import { OrganizationTree } from "./OrganizationTree.tsx";
import { OrganizationCompanyList } from "./OrganizationCompanyList.tsx";
import { RecordDeleteDialog } from "../office/RecordDeleteDialog.tsx";
import { UnitAccessPanel } from "./UnitAccessPanel.tsx";
import { UnitMembersPanel } from "./UnitMembersPanel.tsx";
import { UnitWorkflowSummary } from "./UnitWorkflowSummary.tsx";
import "../../styles/routes/office.css";
import "../../styles/routes/organization.css";

export function OrganizationDirectoryPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  return user.capabilities.canManageUsers || user.capabilities.unitManagement?.organization ? <DirectoryWorkspace client={client} user={user} />
    : <section className="work-surface office-workspace organization-workspace" aria-label="单位管理">{user.capabilities.unitManagement?.appointments && <UnitMembersPanel client={client} user={user} company={user.companyScope} />}{user.capabilities.unitManagement?.roles && <UnitAccessPanel client={client} user={user} company={user.companyScope} />}{user.capabilities.unitManagement?.analytics && <UnitWorkflowSummary client={client} user={user} />}</section>;
}

function DirectoryWorkspace({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const query = useOrganizationDirectory(client);
  const deletion = useOrganizationOperation();
  const [deleting, setDeleting] = useState<{ kind: "company"; record: ApiOrganizationCompanyDto } | { kind: "department"; record: ApiOrganizationDepartmentDto } | null>(null);
  const [companyCode, setCompanyCode] = useState(user.companyScope);
  const [search, setSearch] = useState("");
  const [revealDepartmentCode, setRevealDepartmentCode] = useState<string | null>(null);
  const [companyEditor, setCompanyEditor] = useState<{ record?: ApiOrganizationCompanyDto } | null>(null);
  const [departmentEditor, setDepartmentEditor] = useState<{ record?: ApiOrganizationDepartmentDto; parentCode?: string } | null>(null);
  const companies = query.data?.companies ?? [];
  const company = companies.find((item) => item.code === companyCode) ?? companies.find((item) => item.isActive) ?? companies[0];
  const departments = (query.data?.departments ?? []).filter((item) => item.companyCode === company?.code);
  return <section className="work-surface office-workspace organization-workspace" aria-label="组织架构管理">
    <div className="organization-heading"><div><h2>公司与部门</h2><p className="office-muted">公司与部门确定数据归属；负责人用于部门逐级审批，操作权限在账号与权限配置。员工调岗从人员档案办理。</p></div>
      <div className="organization-actions">{user.capabilities.enabledModules?.includes("office.people") && <Link className="command-button secondary" to="/office/people">人员档案</Link>}
        {user.capabilities.canManageUsers && <Link className="command-button secondary" to="/system/access-control">账号与权限</Link>}
        {user.capabilities.canManageUsers && <button type="button" className="command-button secondary" onClick={() => setCompanyEditor({})}><Plus size={16} aria-hidden="true" />新增公司</button>}</div></div>
    <details><summary>公司、职位与审批如何设置</summary><p className="office-muted">公司下建立实际部门，并在公司信息中指定公司领导审批负责人，可以是董事长、总经理、分管副总等。人员档案维护主职、兼职和直属/职能汇报，审批规则可组合部门负责人、公司领导及指定人员会签。负责人须关联启用账号并取得对应查看、审批范围；组织层级和职位名称不自动授予权限。</p></details>
    {query.isPending ? <PageState tone="loading" title="正在加载组织架构" /> : query.isError ? <PageState tone="error" title="组织架构加载失败" description={readApiError(query.error)}
      action={<button className="command-button secondary" type="button" onClick={() => void query.refetch()}>重新加载</button>} /> : !company ? <PageState title="尚未设置公司" description="先新增公司，再在公司下建立部门。" /> : <>
      <div className="organization-directory-layout">
      <OrganizationCompanyList companies={companies} departments={query.data?.departments ?? []} selectedCode={company.code}
        onSelect={(code) => { setCompanyCode(code); setSearch(""); }} />
      <section className="organization-departments" aria-label="部门层级">
        <div className="organization-company-summary" aria-label="公司信息">
          <div className="organization-company-title"><Building2 size={19} aria-hidden="true" /><h3>{company.name}</h3>
            <span className="organization-state" data-active={company.isActive}>{company.isActive ? "启用" : "停用"}</span></div>
          <div className="organization-actions"><button type="button" className="text-button" onClick={() => setCompanyEditor({ record: company })}>编辑公司</button>
            <button type="button" className="text-button" onClick={() => setDeleting({ kind: "company", record: company })}>删除公司</button></div>
          <p className="office-muted">{company.code}{company.code === user.companyScope ? " · 当前账号所属公司" : ""} · {departments.length} 个部门 · {departments.filter((item) => item.isActive).length} 个启用 · {departments.filter((item) => item.managerEmployeeId).length} 个已设负责人</p>
          {company.unitKind !== "Group" && <p>公司领导审批负责人：{company.executiveName || "未设置"}</p>}
        </div>
        <div className="organization-heading"><div><h3>部门层级</h3></div>
          <button type="button" className="command-button" disabled={!company.isActive || company.unitKind === "Group"} onClick={() => setDepartmentEditor({})}><Plus size={17} aria-hidden="true" />新增部门</button></div>
        <div className="organization-search"><Search size={17} aria-hidden="true" /><input aria-label="查找部门或负责人" type="search" maxLength={100} value={search} onChange={(event) => setSearch(event.target.value)} placeholder="查找部门名称、代码或负责人" />
          <button type="button" className="icon-button" aria-label="刷新组织架构" disabled={query.isFetching} onClick={() => void query.refetch()}><RefreshCw size={17} aria-hidden="true" /></button></div>
        <OrganizationTree key={company.code} departments={departments} keyword={search} companyName={company.name} canAdd={company.isActive && company.unitKind !== "Group"}
          revealDepartmentCode={revealDepartmentCode} onRevealed={() => setRevealDepartmentCode(null)}
          onEdit={(record) => setDepartmentEditor({ record })} onAdd={(parentCode) => setDepartmentEditor({ parentCode })}
          onDelete={(record) => setDeleting({ kind: "department", record })} />
      </section>
      </div>
    </>}
    {company && (user.capabilities.canManageUsers || user.capabilities.unitManagement?.roles) && <UnitAccessPanel key={company.code} client={client} user={user} company={company.code} />}
    {company && (user.capabilities.canManageUsers || user.capabilities.unitManagement?.analytics) && <UnitWorkflowSummary key={`summary:${company.id}`} client={client} user={user} unitId={user.capabilities.canManageUsers ? company.id : undefined} />}
    {company && company.unitKind !== "Group" && (user.capabilities.canManageUsers || user.capabilities.unitManagement?.appointments) && <UnitMembersPanel key={`members:${company.code}`} client={client} user={user} company={company.code} />}
    {companyEditor && <OrganizationCompanyForm client={client} companies={companies} canStructure={user.capabilities.canManageUsers} record={companyEditor.record} onClose={() => setCompanyEditor(null)}
      onSaved={(saved) => { setCompanyCode(saved.code); setCompanyEditor(null); }} />}
    {departmentEditor && company && <OrganizationDepartmentForm client={client} company={company} departments={departments} {...departmentEditor}
      onClose={() => setDepartmentEditor(null)} onSaved={(saved) => { setDepartmentEditor(null); setSearch(""); setRevealDepartmentCode(saved.code); }} />}
    {deleting && <RecordDeleteDialog name={deleting.record.name} version={deleting.record.versionNumber} operation={deletion}
      description="仅可删除没有下级、账号、人员或业务引用的目录项。已有历史的目录项请保留并按需停用。"
      onDelete={(body, signal) => deleting.kind === "company" ? client.deleteOrganizationCompany({ id: deleting.record.id, body }, { signal })
        : client.deleteOrganizationDepartment({ id: deleting.record.id, body }, { signal })}
      onClose={() => setDeleting(null)} onDeleted={() => setDeleting(null)} />}
  </section>;
}
