import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { Button } from "../../ui/Button.tsx";
import { PageState } from "../../ui/PageState.tsx";
import { Field, FilterBar } from "../../ui/primitives/Field.tsx";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";

export function OaFormReferenceInput({ client, company, kind, value, label, required, onChange }: {
  client: ExportDocManagerApiClient; company: string; kind: "Person" | "Department";
  value: string; label: string; required: boolean; onChange: (value: string) => void;
}) {
  const [opened, setOpened] = useState(false);
  const [keyword, setKeyword] = useState("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<{ id: string; name: string }>();
  const query = useQuery({
    queryKey: ["office", "form-references", company, kind, search, page], enabled: opened,
    queryFn: ({ signal }) => client.listOaFormReferences({ kind, keyword: search, pageNumber: page, pageSize: 20 }, { signal }),
  });
  useServerPageNumber(query, page, setPage, 20);
  return <div>
    <Field label={label} required={required}><input readOnly value={selected?.id === value ? selected.name : value} /></Field>
    <div className="office-card-actions">
      <Button aria-expanded={opened} onClick={() => setOpened(!opened)}>{opened ? "收起选项" : kind === "Person" ? "选择人员" : "选择部门"}</Button>
      <Button disabled={!value} onClick={() => onChange("")}>清空</Button>
    </div>
    {opened && <section aria-label={`${label}选项`}>
      <FilterBar label={`${label}搜索`}>
        <Field label="搜索"><input type="search" value={keyword} maxLength={100} onChange={event => setKeyword(event.target.value)} /></Field>
        <Button onClick={() => { setSearch(keyword.trim()); setPage(1); }}>搜索</Button>
      </FilterBar>
      {query.isPending && <PageState tone="loading" title="正在读取选项" />}
      {query.isError && <PageState tone="error" title="选项读取失败" description={readApiError(query.error)} action={<Button onClick={() => void query.refetch()}>重试</Button>} />}
      {query.isSuccess && <>
        <div className="office-card-actions">{query.data.items.map(item => <Button key={item.id} onClick={() => { setSelected(item); onChange(item.id); setOpened(false); }}>{item.name}（{item.id}）</Button>)}</div>
        {query.data.totalCount === 0 && <PageState tone="empty" title="没有匹配选项" />}
        <Pagination pageNumber={page} totalCount={query.data.totalCount} pageSize={20} isBusy={query.isFetching} onPageChange={setPage} />
      </>}
    </section>}
  </div>;
}
