import { useEffect, useLayoutEffect, useMemo, useRef, useState, type CSSProperties } from "react";
import { ChevronDown, ChevronRight, Pencil, Plus, Trash2 } from "lucide-react";
import type { ApiOrganizationDepartmentDto } from "../../api/index.ts";
import { IconButton } from "../../ui/Button.tsx";
import { PageState } from "../../ui/PageState.tsx";
import { buildDepartmentTreeRows, departmentOptions } from "./organizationModel.ts";

type Props = {
  departments: ApiOrganizationDepartmentDto[];
  keyword: string;
  companyName: string;
  onEdit: (department: ApiOrganizationDepartmentDto) => void;
  onAdd: (parentCode: string) => void;
  onDelete: (department: ApiOrganizationDepartmentDto) => void;
  canAdd: boolean;
  revealDepartmentCode: string | null;
  onRevealed: () => void;
};

export function OrganizationTree({ departments, keyword, companyName, canAdd, onEdit, onAdd, onDelete, revealDepartmentCode, onRevealed }: Props) {
  const [expandedDepth, setExpandedDepth] = useState(1);
  const [overrides, setOverrides] = useState<Record<string, boolean>>({});
  const [selectedCode, setSelectedCode] = useState("");
  const searching = Boolean(keyword.trim());
  const rows = useMemo(() => buildDepartmentTreeRows(departments, keyword, expandedDepth, overrides), [departments, keyword, expandedDepth, overrides]);
  const selected = rows.find((row) => row.code === selectedCode);
  const tableRef = useRef<HTMLTableElement>(null);
  const pendingScroll = useRef<string | null>(null);
  useEffect(() => {
    const target = revealDepartmentCode && departmentOptions(departments).find((entry) => entry.code === revealDepartmentCode);
    if (!target) return;
    pendingScroll.current = target.code;
    setOverrides((current) => ({ ...current, ...Object.fromEntries(target.ancestors.map((code) => [code, true])) }));
    setSelectedCode(target.code);
    onRevealed();
  }, [departments, revealDepartmentCode, onRevealed]);
  useLayoutEffect(() => {
    if (!pendingScroll.current) return;
    const row = Array.from(tableRef.current?.rows ?? []).find((entry) => entry.dataset.departmentCode === pendingScroll.current);
    if (row) { row.scrollIntoView({ block: "nearest" }); pendingScroll.current = null; }
  }, [rows]);
  function expandTo(depth: number) {
    setExpandedDepth(depth);
    setOverrides({});
  }
  return <div className="organization-tree-workspace">
    <div className="organization-tree-toolbar">
      <span className="office-muted">{searching ? "搜索结果含上级路径" : Object.keys(overrides).length ? "按需展开" : expandedDepth === 0 ? "仅显示一级" : expandedDepth === 1 ? "展开两级" : "已展开全部"} · 显示 {rows.length} / {departments.length} 个部门</span>
      <div className="organization-actions" role="group" aria-label="部门展开层级">
        <button type="button" className="text-button" disabled={searching} onClick={() => expandTo(0)}>仅看一级</button>
        <button type="button" className="text-button" disabled={searching} onClick={() => expandTo(1)}>展开两级</button>
        <button type="button" className="text-button" disabled={searching} onClick={() => expandTo(Infinity)}>全部展开</button>
      </div>
    </div>
    {rows.length ? <>
      <div className="organization-tree-scroll" role="region" aria-label="部门层级列表" tabIndex={0}>
        <table ref={tableRef} className="organization-tree-table">
          <caption className="visually-hidden">部门按层级排列；展开下级或选择名称查看完整路径。</caption>
          <colgroup><col /><col className="organization-manager-column" /><col className="organization-status-column" /><col className="organization-actions-column" /></colgroup>
          <thead><tr><th scope="col">部门 / 代码</th><th scope="col" className="organization-manager-cell">负责人</th><th scope="col">状态</th><th scope="col">操作</th></tr></thead>
          <tbody>{rows.map((department) => <tr key={department.code} data-department-code={department.code} data-selected={department.code === selectedCode} data-depth={department.depth}>
            <td>
              <div className="organization-node" style={{ "--organization-depth": department.depth } as CSSProperties}>
                {department.childCount > 0 ? <button type="button" className="icon-button organization-expand" disabled={searching}
                  aria-expanded={department.expanded} aria-label={(department.expanded ? "收起" : "展开") + department.name + "的下级部门"}
                  onClick={() => setOverrides((current) => ({ ...current, [department.code]: !department.expanded }))}>
                  {department.expanded ? <ChevronDown size={16} aria-hidden="true" /> : <ChevronRight size={16} aria-hidden="true" />}
                </button> : <span className="organization-leaf" aria-hidden="true" />}
                <button type="button" className="organization-node-name" title={department.label} aria-pressed={department.code === selectedCode}
                  onClick={() => setSelectedCode(department.code)}>
                  <strong>{department.name}</strong><small>{department.code}{department.depth >= 6 ? " · 第 " + (department.depth + 1) + " 级" : ""}</small>
                </button>
                {department.childCount > 0 && <small className="organization-child-count" title="直属下级部门数">{department.childCount} 下级</small>}
              </div>
            </td>
            <td className="organization-manager-cell" title={department.managerName || "未设置"}>{department.managerName || <span className="office-muted">未设置</span>}</td>
            <td><span className="organization-state" data-active={department.isActive}>{department.isActive ? "启用" : "停用"}</span></td>
            <td><div className="organization-row-actions">
              <IconButton label={"编辑" + department.name} onClick={() => onEdit(department)}><Pencil size={15} aria-hidden="true" /></IconButton>
              <IconButton label={"为" + department.name + "新增下级部门"} disabled={!canAdd || !department.isActive} onClick={() => onAdd(department.code)}><Plus size={16} aria-hidden="true" /></IconButton>
              <IconButton label={"删除" + department.name} onClick={() => onDelete(department)}><Trash2 size={15} aria-hidden="true" /></IconButton>
            </div></td>
          </tr>)}</tbody>
        </table>
      </div>
      <div className="organization-selection-path" role="status">
        {selected ? <><strong>{companyName} / {selected.label}</strong><span>部门代码：{selected.code} · 负责人：{selected.managerName || "未设置"} · {selected.isActive ? "启用" : "停用"}</span></>
          : <span>点击部门名称查看完整路径与负责人；行末可编辑、新增下级或删除。</span>}
      </div>
    </> : <PageState title={searching ? "没有符合条件的部门" : "该公司尚未设置部门"}
      description={searching ? "尝试其他部门名称、代码或负责人。" : "点击“新增部门”，按实际公司架构建立部门。"} />}
  </div>;
}
