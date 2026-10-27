import { useMemo, useState } from "react";
import type { ApiHsCodeImportPreviewResponse } from "../../api/index.ts";
import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";

const changes: Record<string, string> = { Add: "新增", Update: "更新", Unchanged: "不变", SuspectedObsolete: "疑似作废", Conflict: "冲突", Invalid: "无效" };
const fields: Record<string, string> = {
  code: "商品编码", name: "商品名称", elements: "申报要素", unit: "法定第一单位", unit2: "法定第二单位",
  supervisionConditions: "海关监管条件", inspectionCategory: "检验检疫类别", rebateRate: "出口退税率",
  normalTariffRate: "普通税率", preferentialTariffRate: "优惠/最惠国税率", exportTariffRate: "出口税率",
  consumptionTaxRate: "消费税率", valueAddedTaxRate: "增值税率", description: "英文描述", notes: "备注",
};

export function HsCodeImportPreview({ preview, busy, onBack, onCommit }: {
  preview: ApiHsCodeImportPreviewResponse;
  busy: boolean;
  onBack: () => void;
  onCommit: () => void;
}) {
  const [filter, setFilter] = useState("");
  const [page, setPage] = useState(1);
  const [size, setSize] = useState(50);
  const rows = useMemo(() => filter ? preview.items.filter(row => row.changeType === filter) : preview.items, [preview.items, filter]);
  const totalPages = Math.max(1, Math.ceil(rows.length / size));
  const currentPage = Math.min(page, totalPages);
  const visible = rows.slice((currentPage - 1) * size, currentPage * size);
  return <div className="hs-code-preview">
    <div className="hs-code-preview-summary">
      <div><span>识别可信度</span><strong>{preview.confidence}%</strong></div>
      <div><span>新增</span><strong>{preview.addCount}</strong></div>
      <div><span>更新</span><strong>{preview.updateCount}</strong></div>
      <div><span>疑似作废</span><strong>{preview.suspectedObsoleteCount}</strong></div>
      <div><span>需处理</span><strong>{preview.conflictCount + preview.invalidCount}</strong></div>
    </div>
    <div className="hs-code-preview-meta">
      <span>工作表：{preview.worksheetName}</span><span>标题行：第 {preview.headerRowNumber} 行</span>
      <span>来源：{preview.sourceName || "未填写"}</span><span>年份：{preview.effectiveYear ?? "未填写"}</span>
    </div>
    {preview.warnings.map(warning => <div className="warning-note" key={warning}>{warning}</div>)}
    <details className="hs-code-column-mapping"><summary>查看识别到的字段映射</summary><div>{preview.columns.map(column => <span key={column.field}>{column.header} → {fields[column.field] ?? column.field}（{column.confidence}%）</span>)}</div></details>
    <label><span>变更类别</span><select aria-label="筛选导入变更" value={filter} onChange={event => { setFilter(event.target.value); setPage(1); }}>
      <option value="">全部变更</option>{Object.entries(changes).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
    </select></label>
    <ResponsiveTableFrame label="HS 编码导入预览" className="hs-code-preview-table-frame" mobileLayout="scroll">
      <table><thead><tr><th>处理</th><th>Excel 行</th><th>编码</th><th>名称</th><th>说明</th></tr></thead><tbody>
        {visible.map((row, index) => <tr key={`${row.changeType}-${row.item.code}-${row.rowNumber}-${index}`}>
          <td><span className={`hs-code-change-badge change-${row.changeType.toLowerCase()}`}>{changes[row.changeType] ?? row.changeType}</span></td>
          <td>{row.rowNumber || "-"}</td><td>{row.item.code || "-"}</td><td>{row.item.name || "-"}</td>
          <td>{row.message}{row.replacementCandidates.length ? ` 候选：${row.replacementCandidates.join("、")}` : ""}</td>
        </tr>)}
        {!visible.length && <tr><td colSpan={5}>此类别没有变更。</td></tr>}
      </tbody></table>
    </ResponsiveTableFrame>
    <ListPaginationControls pageNumber={currentPage} totalPages={totalPages} totalCount={rows.length} pageSize={size} pageSizeOptions={[25, 50, 100]}
      isBusy={busy} onPageChange={setPage} onPageSizeChange={next => { setSize(next); setPage(1); }} />
    <div className="hs-code-preview-actions">
      <button className="command-button secondary" type="button" disabled={busy} onClick={onBack}>重新选择</button>
      <button className="command-button" type="button" disabled={busy || preview.conflictCount + preview.invalidCount > 0 && preview.addCount + preview.updateCount + preview.suspectedObsoleteCount === 0} onClick={onCommit}>{busy ? "正在导入" : "确认导入"}</button>
    </div>
  </div>;
}
