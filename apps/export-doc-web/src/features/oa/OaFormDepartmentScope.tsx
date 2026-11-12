import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { Button } from "../../ui/Button.tsx";
import { PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { Field, FilterBar } from "../../ui/primitives/Field.tsx";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";

export function OaFormDepartmentScope({
  client,
  company,
  value,
  onChange,
}: {
  client: ExportDocManagerApiClient;
  company: string;
  value: string[];
  onChange: (ids: string[]) => void;
}) {
  const [keyword, setKeyword] = useState("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const query = useQuery({
    queryKey: ["office", "form-references", company, "Department", search, page],
    queryFn: ({ signal }) =>
      client.listOaFormReferences({ kind: "Department", keyword: search, pageNumber: page, pageSize: 20 }, { signal }),
  });
  useServerPageNumber(query, page, setPage, 20);
  const selected = new Set(value);
  return (
    <section className="office-field-wide" aria-label="适用部门">
      <h3>适用部门</h3>
      <p>{value.length ? `已选择 ${value.length} 个部门` : "全部部门"}</p>
      <FilterBar label="适用部门搜索">
        <Field label="搜索部门">
          <input type="search" value={keyword} maxLength={100} onChange={(event) => setKeyword(event.target.value)} />
        </Field>
        <Button
          onClick={() => {
            setSearch(keyword.trim());
            setPage(1);
          }}
        >
          搜索
        </Button>
        <Button disabled={!value.length} onClick={() => onChange([])}>
          改为全部部门
        </Button>
      </FilterBar>
      {query.isPending && <PageState tone="loading" title="正在读取部门" />}
      {query.isError && (
        <PageState
          tone="error"
          title="部门读取失败"
          description={readApiError(query.error)}
          action={<Button onClick={() => void query.refetch()}>重试</Button>}
        />
      )}
      <div className="office-card-actions">
        {query.data?.items.map((item) => (
          <label className="checkbox-field" key={item.id}>
            <input
              type="checkbox"
              checked={selected.has(item.id)}
              onChange={(event) =>
                onChange(event.target.checked ? [...value, item.id] : value.filter((id) => id !== item.id))
              }
            />
            {item.name}（{item.id}）
          </label>
        ))}
      </div>
      {value.length > 0 && (
        <details>
          <summary>已选部门编号</summary>
          {value.map((id) => (
            <Button key={id} onClick={() => onChange(value.filter((item) => item !== id))}>
              移除 {id}
            </Button>
          ))}
        </details>
      )}
      <Pagination
        pageNumber={page}
        totalCount={query.data?.totalCount ?? 0}
        pageSize={20}
        isBusy={query.isFetching}
        onPageChange={setPage}
      />
    </section>
  );
}
