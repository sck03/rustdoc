import { useMemo, useState } from "react";
import { ResponsiveTableFrame } from "./ResponsiveTable.tsx";
import { Field } from "./primitives/Field.tsx";
import { Pagination } from "./primitives/Pagination.tsx";

interface ImportRow {
  rowNumber: number; name: string; contactName: string; error: string; isDuplicate: boolean;
  countryRegion?: string; category?: string;
}
type RowFilter = "all" | "invalid" | "duplicate" | "valid";

/** The bounded preview snapshot is shared by both directories; only one page is mounted. */
export function DirectoryImportPreview({ rows, supplier = false, busy }: {
  rows: readonly ImportRow[]; supplier?: boolean; busy: boolean;
}) {
  const [filter, setFilter] = useState<RowFilter>("all");
  const [page, setPage] = useState(1);
  const [size, setSize] = useState(30);
  const filtered = useMemo(() => rows.filter(row => filter === "all" || (filter === "invalid" ? Boolean(row.error)
    : filter === "duplicate" ? row.isDuplicate : !row.error && !row.isDuplicate)), [rows, filter]);
  const pageNumber = Math.min(page, Math.max(1, Math.ceil(filtered.length / size)));
  const visible = filtered.slice((pageNumber - 1) * size, pageNumber * size);
  return <>
    <Field label="预检结果" inline><select value={filter} disabled={busy} onChange={event => { setFilter(event.target.value as RowFilter); setPage(1); }}>
      <option value="all">全部</option><option value="invalid">错误行</option><option value="duplicate">重复行</option><option value="valid">可导入</option>
    </select></Field>
    <ResponsiveTableFrame label={supplier ? "供应商导入预览" : "CRM 客户导入预览"} mobileLayout="scroll" busy={busy}>
      <table className="data-table"><thead><tr><th>行</th><th>{supplier ? "供应商" : "客户"}</th><th>{supplier ? "分类" : "国家"}</th><th>联系人</th><th>结果</th></tr></thead>
        <tbody>{visible.map(row => <tr key={row.rowNumber}><td>{row.rowNumber}</td><td>{row.name || "-"}</td><td>{(supplier ? row.category : row.countryRegion) || "-"}</td><td>{row.contactName || "-"}</td><td>{row.error || (row.isDuplicate ? "重复，跳过" : "可导入")}</td></tr>)}
          {!visible.length && <tr><td colSpan={5}>没有符合条件的预检行。</td></tr>}
        </tbody>
      </table>
    </ResponsiveTableFrame>
    <Pagination pageNumber={pageNumber} totalCount={filtered.length} pageSize={size} pageSizeOptions={[30, 50, 100]} isBusy={busy}
      onPageChange={setPage} onPageSizeChange={value => { setSize(value); setPage(1); }} />
  </>;
}
