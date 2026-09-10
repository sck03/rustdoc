import { useState } from "react";
import { ChevronDown, ChevronRight, Network, UserRound } from "lucide-react";
import type { ApiOrganizationDepartmentDto } from "../../api/index.ts";

type Props = { departments: ApiOrganizationDepartmentDto[]; onEdit: (department: ApiOrganizationDepartmentDto) => void; onAdd: (parentCode: string) => void;
  onDelete: (department: ApiOrganizationDepartmentDto) => void; canAdd: boolean };

export function OrganizationTree(props: Props) {
  const groups = new Map<string, ApiOrganizationDepartmentDto[]>();
  for (const item of props.departments) {
    const key = item.parentCode ?? "";
    const group = groups.get(key);
    if (group) group.push(item); else groups.set(key, [item]);
  }
  for (const items of groups.values()) items.sort((left, right) => left.name.localeCompare(right.name, "zh-CN") || left.code.localeCompare(right.code));
  return <ul className="organization-tree" aria-label="部门架构树">{groups.get("")?.map((department) =>
    <DepartmentBranch key={department.code} {...props} department={department} groups={groups} depth={0} />)}</ul>;
}

function DepartmentBranch({ department, groups, depth, ...props }: Props & {
  department: ApiOrganizationDepartmentDto; groups: Map<string, ApiOrganizationDepartmentDto[]>; depth: number;
}) {
  const [open, setOpen] = useState(true);
  const children = groups.get(department.code) ?? [];
  return <li>
    <div className="organization-node" data-root={depth === 0} style={{ marginInlineStart: `${Math.min(depth, 4) * 1.25}rem` }}>
      {children.length > 0 ? <button type="button" className="icon-button" aria-expanded={open}
        aria-label={`${open ? "收起" : "展开"}${department.name}的下级部门`} onClick={() => setOpen((value) => !value)}>
        {open ? <ChevronDown size={18} aria-hidden="true" /> : <ChevronRight size={18} aria-hidden="true" />}</button> : <span className="organization-leaf" aria-hidden="true" />}
      <Network className="organization-node-icon" size={19} aria-hidden="true" />
      <div className="organization-node-name"><strong>{department.name}</strong><small>{department.code} · {depth === 0 ? "直属部门" : `第 ${depth + 1} 层部门`}{children.length > 0 ? ` · ${children.length} 个直属下级` : ""}</small></div>
      <span className="organization-manager"><UserRound size={15} aria-hidden="true" />{department.managerName || "待设负责人"}</span>
      {!department.isActive && <span className="office-badge">已停用</span>}
      <div className="organization-actions"><button type="button" className="command-button secondary" onClick={() => props.onEdit(department)} aria-label={`编辑${department.name}`}>编辑</button>
        {props.canAdd && department.isActive && <button type="button" className="command-button secondary" onClick={() => props.onAdd(department.code)} aria-label={`为${department.name}新增下级部门`}>新增下级</button>}</div>
      <button type="button" className="command-button secondary organization-delete" onClick={() => props.onDelete(department)} aria-label={`删除${department.name}`}>删除</button>
    </div>
    {open && children.length > 0 && <ul>{children.map((child) => <DepartmentBranch key={child.code} {...props} department={child} groups={groups} depth={depth + 1} />)}</ul>}
  </li>;
}
