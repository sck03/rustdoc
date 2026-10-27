import { useEffect, useState } from "react";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";

type Row = { id: number; code: string; name: string; updateTime?: string | null };

export function HsCodeMaintenanceTable({ items, busy, selectedIds, onSelectionChange }: {
  items: Row[]; busy: boolean; selectedIds?: Set<number>; onSelectionChange?: (ids: Set<number>) => void;
}) {
  const [page, setPage] = useState(1);
  const [size, setSize] = useState(50);
  useEffect(() => setPage(1), [items]);
  const pages = Math.max(1, Math.ceil(items.length / size));
  const current = Math.min(page, pages);
  const selectionEnabled = Boolean(selectedIds && onSelectionChange);
  return <div>
    <ResponsiveTableFrame label="HS 编码维护预检" mobileLayout="scroll">
      <table><thead><tr>
        {selectionEnabled && <th><input type="checkbox" aria-label="选择本批全部编码" disabled={busy || !items.length}
          checked={items.length > 0 && items.every(row => selectedIds!.has(row.id))}
          onChange={event => onSelectionChange?.(new Set(event.target.checked ? items.map(row => row.id) : []))} /></th>}
        <th>编码</th><th>名称</th><th>最后更新时间</th>
      </tr></thead><tbody>
        {items.slice((current - 1) * size, current * size).map(row => <tr key={row.id}>
          {selectionEnabled && <td><input type="checkbox" aria-label={`选择编码 ${row.code}`} disabled={busy} checked={selectedIds!.has(row.id)} onChange={event => {
            const next = new Set(selectedIds); if (event.target.checked) next.add(row.id); else next.delete(row.id); onSelectionChange?.(next);
          }} /></td>}
          <td>{row.code}</td><td>{row.name}</td><td>{row.updateTime || "不明"}</td>
        </tr>)}
        {!items.length && <tr><td colSpan={selectionEnabled ? 4 : 3}>本批没有符合条件的编码。</td></tr>}
      </tbody></table>
    </ResponsiveTableFrame>
    <ListPaginationControls pageNumber={current} totalPages={pages} totalCount={items.length} pageSize={size} pageSizeOptions={[25, 50, 100]}
      isBusy={busy} onPageChange={setPage} onPageSizeChange={next => { setSize(next); setPage(1); }} />
  </div>;
}
