import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { useDebouncedValue } from "../../ui/useDebouncedValue.ts";
import { readApiError } from "../../ui/formUtils.ts";

type Props = {
  client: ExportDocManagerApiClient; unitId: string; kind: "projects" | "departments";
  label: string; values: string[]; retainedIds?: string[]; multiple?: boolean; onChange(values: string[]): void;
};

export function AssignmentOptionPicker({ client, unitId, kind, label, values, retainedIds, multiple, onChange }: Props) {
  const [opened, setOpened] = useState(false);
  const [keyword, setKeyword] = useState("");
  const [page, setPage] = useState(1);
  const search = useDebouncedValue(keyword, 250);
  const selectedIds = [...new Set(retainedIds ?? values)].sort().join(",");
  const query = useQuery({
    enabled: opened || values.length > 0,
    queryKey: ["personnel-foundation", unitId, "assignment-options", kind, search, page, selectedIds],
    queryFn: ({ signal }) => client.getIdentityAssignmentOptions({ unitId, kind, keyword: search, pageNumber: page, pageSize: 20, selectedIds }, { signal }),
  });
  const rows = query.data?.items ?? [];
  const selected = query.data?.selected ?? [];
  const labelFor = (id: string) => {
    const row = selected.find(row => row.id === id) ?? rows.find(row => row.id === id);
    return row ? `${row.name} · ${row.code}${row.isActive ? "" : "（停用）"}` : `已选编号 ${id}`;
  };
  return <section aria-label={label}><details onToggle={event => setOpened(event.currentTarget.open)}>
    <summary>{label}{values.length ? ` · ${multiple ? `已选 ${values.length} 项` : labelFor(values[0])}` : " · 不限定"}</summary>
    {opened && <>
    <OfficeField label={`查找${kind === "projects" ? "项目" : "部门"}`}><input type="search" maxLength={100} value={keyword}
      onChange={event => { setKeyword(event.target.value); setPage(1); }} /></OfficeField>
    {query.isError && <p role="alert">{readApiError(query.error)} <button type="button" onClick={() => void query.refetch()}>重试</button></p>}
    {multiple ? <fieldset><legend>{label}</legend>
      {values.map(id => <label key={id} className="checkbox-field"><input type="checkbox" checked onChange={() => onChange(values.filter(value => value !== id))} />{labelFor(id)}</label>)}
      {rows.filter(row => !values.includes(row.id)).map(row => <label key={row.id} className="checkbox-field"><input type="checkbox" checked={false} disabled={values.length >= 200}
        onChange={() => onChange([...values, row.id])} />{row.name} · {row.code}</label>)}
    </fieldset> : <OfficeField label={label}><select value={values[0] ?? ""} onChange={event => onChange(event.target.value ? [event.target.value] : [])}>
      <option value="">不限定</option>
      {values.filter(id => !rows.some(row => row.id === id)).map(id => <option key={id} value={id}>{labelFor(id)}</option>)}
      {rows.map(row => <option key={row.id} value={row.id}>{row.name} · {row.code}</option>)}
    </select></OfficeField>}
    {(query.data?.totalCount ?? 0) > 20 && <Pagination pageNumber={page} pageSize={20} totalCount={query.data?.totalCount ?? 0} isBusy={query.isFetching} onPageChange={setPage} />}
    </>}
  </details></section>;
}
