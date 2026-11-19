import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import { OperationFeedback, requestErrorFeedback } from "../../ui/OperationFeedback.tsx";
import { TablePrimaryText } from "../../ui/TablePrimaryText.tsx";
import { TaskViewTabs, getTaskViewPanelProps } from "../../ui/TaskViewTabs.tsx";
import { dateInputToApiDate } from "../../ui/formUtils.ts";
import { SupplierAssessmentAnalytics } from "./SupplierAssessmentAnalytics.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { ServerDraftUpdateNotice } from "../../ui/serverDraftSync.tsx";
import { useSupplierAssessments } from "./useSupplierAssessments.ts";

export function SupplierAssessmentsPanel({ businessDate, client, supplierId, supplierName, canCreate, canEdit, canApprove, canDelete }: {
  businessDate: string; client: ExportDocManagerApiClient; supplierId: number; supplierName: string;
  canCreate: boolean; canEdit: boolean; canApprove: boolean; canDelete: boolean;
}) {
  const today = dateInputToApiDate(businessDate);
  if (!today) throw new Error("服务端业务日期无效。");
  const { query, draft, view, feedback, command, select, changeView, save, analyticsQuery, loadServer,
    pageNumber, pageSize, setPageNumber, setPageSize } = useSupplierAssessments(client, supplierId, today,
      { create: canCreate, edit: canEdit, approve: canApprove, delete: canDelete });
  const rows = query.data?.items ?? [];
  const selected = draft.record?.id ? draft.record : undefined;
  const tabsId = `supplier-assessment-${supplierId}`;
  const openEditor = (id: number) => select(rows.find(row => row.id === id) ?? null);
  return <section className="form-section supplier-assessment-workspace">
    <div className="section-header">
      <h3>{view === "directory" ? "供应商评价记录" : view === "analytics" ? "供应商评价分析" : selected ? "编辑供应商评价" : "新增供应商评价"}</h3>
      <span>{query.data?.totalCount ?? 0} 次</span>
    </div>
    <OperationFeedback feedback={feedback ?? (query.isError ? requestErrorFeedback(query.error) : analyticsQuery.isError ? requestErrorFeedback(analyticsQuery.error) : null)} />
    {draft.hasPendingServerVersion && <ServerDraftUpdateNotice entityLabel="供应商评价" onKeepLocal={draft.keepLocalDraft} onLoadServer={() => void loadServer()} />}
    <TaskViewTabs idPrefix={tabsId} value={view} label="供应商评价工作区" onChange={changeView} items={[
      { id: "directory", label: "评价记录" },
      { id: "analytics", label: "趋势分析" },
      { id: "editor", label: selected ? canEdit && selected.status !== "Confirmed" ? "编辑评价" : "查看评价" : "记录评价", disabled: !selected && !canCreate },
    ]} />
    {view === "directory" ? <div {...getTaskViewPanelProps(tabsId, "directory")}>
      <div className="section-header-actions supplier-assessment-actions">
        {canCreate ? <button className="primary-button" type="button" onClick={() => void openEditor(0)}>记录新评价</button> : null}
      </div>
      <ResponsiveTableFrame label="供应商评价记录" mobileLayout="scroll" busy={query.isFetching}><table className="data-table responsive-data-table"><thead><tr>
        <th>日期与类型</th><th>综合分</th><th data-table-priority="secondary">质量</th><th data-table-priority="secondary">交期</th>
        <th data-table-priority="secondary">服务</th><th data-table-priority="secondary">价格</th><th>结论</th><th>备注</th><th />
      </tr></thead><tbody>
        {rows.map((item) => <tr key={item.id}>
          <td><TablePrimaryText value={item.assessmentDate} secondary={item.assessmentKind} /></td>
          <td><strong>{item.averageScore.toFixed(2)}</strong> / 5</td>
          <td data-table-priority="secondary">{item.qualityScore}</td><td data-table-priority="secondary">{item.deliveryScore}</td>
          <td data-table-priority="secondary">{item.serviceScore}</td><td data-table-priority="secondary">{item.priceScore}</td>
          <td><div className="table-row-actions"><BusinessStatusBadge value={item.conclusion} /><BusinessStatusBadge value={item.status === "Confirmed" ? "已确认" : "草稿"} /></div></td><td><TablePrimaryText value={item.notes || "-"} secondary={item.status === "Confirmed" ? `${item.confirmedBy || item.assessedBy || "-"} · 已确认` : item.assessedBy || undefined} /></td>
          <td><button className="secondary-button" type="button" onClick={() => void openEditor(item.id)}>{canEdit && item.status !== "Confirmed" ? "编辑" : "查看"}</button></td>
        </tr>)}
        {!query.isFetching && !query.isError && !rows.length ? <tr><td className="empty-cell" colSpan={9}><div className="empty-cell-content"><strong>尚未记录供应商评价</strong>
          {canCreate ? <button className="primary-button" type="button" onClick={() => void openEditor(0)}>记录第一次评价</button> : null}
        </div></td></tr> : null}
      </tbody></table></ResponsiveTableFrame>
      <Pagination pageNumber={pageNumber} totalPages={query.data?.totalPages ?? 1} totalCount={query.data?.totalCount ?? 0} pageSize={pageSize} pageSizeOptions={[20,50,100]} isBusy={query.isFetching} onPageChange={setPageNumber} onPageSizeChange={setPageSize} />
    </div> : view === "analytics" ? <div {...getTaskViewPanelProps(tabsId, "analytics")}>{analyticsQuery.data ? <SupplierAssessmentAnalytics summary={analyticsQuery.data} supplierName={supplierName} onCreate={canCreate ? () => void openEditor(0) : undefined} /> : <p role="status">{analyticsQuery.isFetching ? "正在加载评价分析…" : ""}</p>}</div> : <form className="form-grid" onSubmit={save} {...getTaskViewPanelProps(tabsId, "editor")}>
      <div className="section-heading-row"><h4>{selected ? canEdit && selected.status !== "Confirmed" ? "编辑评价" : "查看评价" : "记录新评价"}</h4><button className="secondary-button" type="button" onClick={() => void changeView("directory")}>返回评价记录</button></div>
      <div className="form-field-wide context-strip"><strong>{supplierName}</strong><span>1 分表示明显不足，5 分表示表现优秀。</span></div>
      <fieldset className="permission-fieldset form-field-wide" disabled={command.isEditingLocked || (selected ? !canEdit || selected.status === "Confirmed" : !canCreate)}>
      <label>评价日期<input name="assessmentDate" type="date" required max={today} {...draft.field("assessmentDate")} /></label>
      <label>评价类型<select name="assessmentKind" value={draft.values.assessmentKind} onChange={event => draft.change("assessmentKind", event.target.value)}><option>定期评价</option><option>订单复盘</option><option>样品评估</option><option>其它</option></select></label>
      <ScoreField name="qualityScore" label="质量评分" value={draft.values.qualityScore} onChange={value => draft.change("qualityScore", value)} />
      <ScoreField name="deliveryScore" label="交期评分" value={draft.values.deliveryScore} onChange={value => draft.change("deliveryScore", value)} />
      <ScoreField name="serviceScore" label="服务评分" value={draft.values.serviceScore} onChange={value => draft.change("serviceScore", value)} />
      <ScoreField name="priceScore" label="价格评分" value={draft.values.priceScore} onChange={value => draft.change("priceScore", value)} />
      <label>评价结论<select name="conclusion" value={draft.values.conclusion} onChange={event => draft.change("conclusion", event.target.value)}><option>优先合作</option><option>合格</option><option>观察</option><option>暂停合作</option></select></label>
      <label className="form-field-wide">评价备注<textarea name="notes" maxLength={1000} {...draft.field("notes")} placeholder="记录事实、改进事项和下次复核重点" /></label>
      </fieldset>
      <div className="form-actions">{(selected ? canEdit && selected.status !== "Confirmed" : canCreate) ? <button className="primary-button" type="submit" disabled={command.isPending}>保存评价草稿</button> : null}{selected && canApprove && selected.status !== "Confirmed" ? <button className="primary-button" type="button" disabled={command.isPending} onClick={() => command.mutate("confirm")}>确认评价</button> : null}{selected && canDelete && selected.status !== "Confirmed" ? <button className="secondary-button danger-button" type="button" disabled={command.isPending} onClick={() => command.mutate("delete")}>删除草稿</button> : null}</div>
    </form>}
  </section>;
}

function ScoreField({ name, label, value, onChange }: { name: string; label: string; value: string; onChange: (value: string) => void }) {
  return <label>{label}<select name={name} value={value} onChange={event => onChange(event.target.value)}>
    <option value="1">1 - 明显不足</option><option value="2">2 - 需要改进</option><option value="3">3 - 基本符合</option>
    <option value="4">4 - 表现良好</option><option value="5">5 - 表现优秀</option>
  </select></label>;
}
