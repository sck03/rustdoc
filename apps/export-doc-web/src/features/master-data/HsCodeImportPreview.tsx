import type { ApiHsCodeImportPreviewResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { Field } from "../../ui/primitives/Field.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { PageState } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { hsImportChanges as changes, useHsImportPreview, type HsImportChange } from "./useHsImportPreview.ts";

const fields: Record<string, string> = {
  code: "商品编码", name: "商品名称", elements: "申报要素", unit: "法定第一单位", unit2: "法定第二单位",
  supervisionConditions: "海关监管条件", inspectionCategory: "检验检疫类别", rebateRate: "出口退税率",
  normalTariffRate: "普通税率", preferentialTariffRate: "优惠/最惠国税率", exportTariffRate: "出口税率",
  consumptionTaxRate: "消费税率", valueAddedTaxRate: "增值税率", description: "英文描述", notes: "备注",
  status: "状态", sourceName: "资料来源", effectiveYear: "适用年份",
};

export function HsCodeImportPreview({ client, preview, busy, onBack, onCommit }: {
  client: ExportDocManagerApiClient;
  preview: ApiHsCodeImportPreviewResponse;
  busy: boolean;
  onBack: () => void;
  onCommit: () => void;
}) {
  const { query, filter, page, size, setFilter, setPage, setSize } = useHsImportPreview(client, preview.token);
  const visible = query.data?.items ?? [];
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
    <Field label="变更类别" inline><select aria-label="筛选导入变更" disabled={busy} value={filter} onChange={event => setFilter(event.target.value as HsImportChange | "")}>
      <option value="">全部变更</option>{Object.entries(changes).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
    </select></Field>
    {query.isError ? <PageState tone="error" title="预检明细读取失败" description={readApiError(query.error)} action={<button type="button" className="command-button secondary" onClick={() => void query.refetch()}>重试</button>} /> : query.isPending ? <PageState tone="loading" title="正在读取预检明细" /> : <ResponsiveTableFrame label="HS 编码导入预览" className="hs-code-preview-table-frame" mobileLayout="scroll" busy={query.isFetching}>
      <table><thead><tr><th>处理</th><th>Excel 行</th><th>编码</th><th>名称</th><th>说明</th></tr></thead><tbody>
        {visible.map((row, index) => <tr key={`${row.changeType}-${row.item.code}-${row.rowNumber}-${index}`}>
          <td><span className={`hs-code-change-badge change-${row.changeType.toLowerCase()}`}>{changes[row.changeType as HsImportChange] ?? row.changeType}</span></td>
          <td>{row.rowNumber || "-"}</td><td>{row.item.code || "-"}</td><td>{row.item.name || "-"}</td>
          <td>{row.message}{row.replacementCandidates.length ? ` 候选：${row.replacementCandidates.join("、")}` : ""}
            {row.changes.length > 0 && <details><summary>变更 {row.changes.length} 项</summary><ul>{row.changes.map(change =>
              <li key={change.field}>{fields[change.field] ?? change.field}：{change.before || "（空）"} → {change.after || "（空）"}</li>)}</ul></details>}
          </td>
        </tr>)}
        {!visible.length && <tr><td colSpan={5}>此类别没有变更。</td></tr>}
      </tbody></table>
    </ResponsiveTableFrame>}
    <Pagination pageNumber={page} totalPages={query.data?.totalPages ?? 0} totalCount={query.data?.totalCount ?? 0} pageSize={size} pageSizeOptions={[25, 50, 100]}
      isBusy={busy || query.isFetching} onPageChange={setPage} onPageSizeChange={setSize} />
    <div className="hs-code-preview-actions">
      <button className="command-button secondary" type="button" disabled={busy} onClick={onBack}>重新选择</button>
      <button className="command-button" type="button" disabled={busy || query.isPending || query.isError || preview.conflictCount + preview.invalidCount > 0 && preview.addCount + preview.updateCount + preview.suspectedObsoleteCount === 0} onClick={onCommit}>{busy ? "正在导入" : "确认导入"}</button>
    </div>
  </div>;
}
