import type { ApiUserDto, ExportDocManagerApiClient, PersonnelDirectoryRecord } from "../../api/index.ts";
import { useEffect, useId, useRef, useState } from "react";
import { Check, ChevronDown, Search } from "lucide-react";
import { useEmployeeSearch } from "./useEmployeeSearch.ts";
import "../../styles/routes/employee-picker.css";

export function OfficeEmployeePicker({ client, user, value, onChange, disabled }: {
  client: ExportDocManagerApiClient; user: ApiUserDto; value: PersonnelDirectoryRecord | null;
  onChange: (employee: PersonnelDirectoryRecord | null) => void; disabled: boolean;
}) {
  const id = useId();
  const input = useRef<HTMLInputElement>(null);
  const list = useRef<HTMLUListElement>(null);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);
  const m = useEmployeeSearch(client, user, open && !disabled);
  const expanded = open && !disabled;
  useEffect(() => { list.current?.children[active]?.scrollIntoView({ block: "nearest" }); }, [active]);
  function choose(employee: PersonnelDirectoryRecord) {
    onChange(employee); setOpen(false); setActive(-1); m.setKeyword(""); input.current?.focus();
  }
  function show() { if (!open) { m.setKeyword(""); setActive(-1); setOpen(true); } }
  const selectedLabel = value ? `${value.fullName} · ${value.employeeNumber} · ${value.departmentName}` : "";
  return <div className="employee-picker office-field-wide" onBlur={e => {
    if (!e.currentTarget.contains(e.relatedTarget)) { setOpen(false); setActive(-1); }
  }}>
    <label htmlFor={id}>登记人员</label>
    <div className="employee-picker-control" onClick={() => { input.current?.focus(); show(); }}>
      <Search size={18} aria-hidden="true" />
      <input ref={input} id={id} role="combobox" autoComplete="off" maxLength={100} disabled={disabled}
        aria-expanded={expanded} aria-controls={expanded ? `${id}-options` : undefined} aria-autocomplete="list" aria-describedby={`${id}-hint`}
        aria-activedescendant={expanded && m.items[active] ? `${id}-${m.items[active].id}` : undefined}
        placeholder={value && expanded ? `当前：${selectedLabel}，输入可更换` : "输入姓名或工号，选择在职人员"}
        value={expanded ? m.keyword : selectedLabel}
        onFocus={show} onClick={show} onChange={e => { m.setKeyword(e.target.value); setActive(-1); setOpen(true); }}
        onCompositionStart={() => m.setComposing(true)} onCompositionEnd={() => m.setComposing(false)}
        onKeyDown={e => {
          if (e.nativeEvent.isComposing || m.composing) return;
          if (e.key === "Escape" && expanded) { e.preventDefault(); e.stopPropagation(); setOpen(false); }
          if (e.key === "ArrowDown" || e.key === "ArrowUp") {
            e.preventDefault(); show(); setActive(a => !m.items.length ? -1 : a < 0 ? (e.key === "ArrowDown" ? 0 : m.items.length - 1) : (a + (e.key === "ArrowDown" ? 1 : -1) + m.items.length) % m.items.length);
          }
          if (e.key === "Enter" && expanded) { e.preventDefault(); if (m.items[active]) choose(m.items[active]); }
        }} />
      <ChevronDown size={16} aria-hidden="true" />
    </div>
    {expanded && <div className="employee-picker-popup">
      <div className="employee-picker-status" role="status">
        {m.waiting || m.query.isPending ? "正在查找人员…" : m.query.isError ? "人员加载失败，请重试。" : m.items.length ? `找到 ${m.query.data?.totalCount ?? 0} 位在职人员${(m.query.data?.totalCount ?? 0) > 50 ? "，请输入更多文字缩小范围" : "，请选择"}` : "没有匹配的在职人员，请更换姓名或工号。"}
      </div>
      {m.query.isError && <button type="button" className="command-button secondary" onClick={() => void m.query.refetch()}>重新加载人员</button>}
      <ul ref={list} id={`${id}-options`} role="listbox" aria-label="在职人员">
        {m.items.map((employee, index) => <li key={employee.id} id={`${id}-${employee.id}`} role="option"
          aria-selected={employee.id === value?.id} data-active={active === index} onPointerMove={() => setActive(index)}
          onMouseDown={e => e.preventDefault()} onClick={() => choose(employee)}>
          <span className="employee-picker-avatar" aria-hidden="true">{employee.fullName.slice(0, 1)}</span>
          <span className="employee-picker-person"><strong>{employee.fullName}</strong><small>工号 {employee.employeeNumber} · {employee.departmentName}{employee.jobTitle ? ` · ${employee.jobTitle}` : ""}</small></span>
          {employee.id === value?.id && <Check size={18} aria-hidden="true" />}
        </li>)}
      </ul>
    </div>}
    <small id={`${id}-hint`} className="office-muted">{value ? "已选人员；点击上方可搜索并更换。" : "支持姓名、工号搜索；↑ ↓ 选择，Enter 确认。"}</small>
  </div>;
}
