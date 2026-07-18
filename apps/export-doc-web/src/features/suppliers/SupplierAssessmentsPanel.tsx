import { useEffect, useState, type FormEvent } from "react";
import type { ApiSupplierAssessmentDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import { OperationFeedback, errorFeedback, successFeedback, type OperationFeedbackState } from "../../ui/OperationFeedback.tsx";
import { TablePrimaryText } from "../../ui/TablePrimaryText.tsx";
import { TaskViewTabs } from "../../ui/TaskViewTabs.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { SupplierAssessmentAnalytics } from "./SupplierAssessmentAnalytics.tsx";

type AssessmentView = "directory" | "analytics" | "editor";

export function SupplierAssessmentsPanel({ client, supplierId, supplierName, canOperate, canManage }: {
  client: ExportDocManagerApiClient;
  supplierId: number;
  supplierName: string;
  canOperate: boolean;
  canManage: boolean;
}) {
  const [rows, setRows] = useState<ApiSupplierAssessmentDto[]>([]);
  const [selectedId, setSelectedId] = useState(0);
  const [view, setView] = useState<AssessmentView>("directory");
  const [feedback, setFeedback] = useState<OperationFeedbackState | null>(null);
  const selected = rows.find((item) => item.id === selectedId);

  async function load(preferredId?: number) {
    const result = await client.listSupplierAssessments({ supplierId });
    setRows(result);
    setSelectedId(preferredId && result.some((item) => item.id === preferredId) ? preferredId : result[0]?.id ?? 0);
  }

  useEffect(() => {
    setView("directory");
    void load().catch((error) => setFeedback(errorFeedback(readApiError(error))));
  }, [client, supplierId]);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canOperate) return;
    const form = new FormData(event.currentTarget);
    const id = selected?.id ?? 0;
    const assessedDate = text(form, "assessedAt");
    const body = {
      id,
      supplierCompanyId: supplierId,
      assessedAt: `${assessedDate}T12:00:00Z`,
      assessmentKind: text(form, "assessmentKind"),
      qualityScore: number(form, "qualityScore"),
      deliveryScore: number(form, "deliveryScore"),
      serviceScore: number(form, "serviceScore"),
      priceScore: number(form, "priceScore"),
      conclusion: text(form, "conclusion"),
      notes: text(form, "notes"),
      expectedVersion: id > 0 ? selected?.versionNumber ?? 0 : 0,
    };
    try {
      const saved = id
        ? await client.updateSupplierAssessment({ supplierId, id, body })
        : await client.createSupplierAssessment({ supplierId, body });
      await load(saved.id);
      setView("editor");
      setFeedback(successFeedback(id ? "供应商评价已更新。" : "供应商评价已记录。"));
    } catch (error) {
      setFeedback(errorFeedback(readApiError(error)));
    }
  }

  async function remove() {
    if (!canManage || !selected || !window.confirm(`删除 ${selected.assessedAt.slice(0, 10)} 的供应商评价？`)) return;
    try {
      await client.deleteSupplierAssessment({ supplierId, id: selected.id });
      await load();
      setView("directory");
      setFeedback(successFeedback("供应商评价已删除。"));
    } catch (error) {
      setFeedback(errorFeedback(readApiError(error)));
    }
  }

  const today = new Date().toISOString().slice(0, 10);
  return <section className="form-section supplier-assessment-workspace">
    <div className="section-header">
      <div><h3>{view === "directory" ? "供应商评价记录" : view === "analytics" ? "供应商评价分析" : selected ? "编辑供应商评价" : "新增供应商评价"}</h3>
        <p className="section-description">按质量、交期、服务和价格记录 {supplierName} 的阶段表现，评价不会修改供应商主资料。</p></div>
      <span>{rows.length} 次</span>
    </div>
    <OperationFeedback feedback={feedback} />
    <TaskViewTabs value={view} label="供应商评价工作区" onChange={setView} items={[
      { id: "directory", label: "评价记录" },
      { id: "analytics", label: "趋势分析" },
      { id: "editor", label: selected ? canOperate ? "编辑评价" : "查看评价" : "记录评价", disabled: !selected && !canOperate },
    ]} />
    {view === "directory" ? <>
      <div className="section-header-actions supplier-assessment-actions">
        {canOperate ? <button className="primary-button" type="button" onClick={() => { setSelectedId(0); setView("editor"); }}>记录新评价</button> : null}
      </div>
      <div className="table-frame"><table className="data-table responsive-data-table"><thead><tr>
        <th>日期与类型</th><th>综合分</th><th data-table-priority="secondary">质量</th><th data-table-priority="secondary">交期</th>
        <th data-table-priority="secondary">服务</th><th data-table-priority="secondary">价格</th><th>结论</th><th>备注</th><th />
      </tr></thead><tbody>
        {rows.map((item) => <tr key={item.id}>
          <td><TablePrimaryText value={item.assessedAt.slice(0, 10)} secondary={item.assessmentKind} /></td>
          <td><strong>{item.averageScore.toFixed(2)}</strong> / 5</td>
          <td data-table-priority="secondary">{item.qualityScore}</td><td data-table-priority="secondary">{item.deliveryScore}</td>
          <td data-table-priority="secondary">{item.serviceScore}</td><td data-table-priority="secondary">{item.priceScore}</td>
          <td><BusinessStatusBadge value={item.conclusion} /></td><td><TablePrimaryText value={item.notes || "-"} secondary={item.assessedBy || undefined} /></td>
          <td><button className="secondary-button" type="button" onClick={() => { setSelectedId(item.id); setView("editor"); }}>{canOperate ? "编辑" : "查看"}</button></td>
        </tr>)}
        {!rows.length ? <tr><td className="empty-cell" colSpan={9}><div className="empty-cell-content"><strong>尚未记录供应商评价</strong>
          <span>完成样品、订单或阶段合作后，再用四项评分留下可复核依据。</span>
          {canOperate ? <button className="primary-button" type="button" onClick={() => { setSelectedId(0); setView("editor"); }}>记录第一次评价</button> : null}
        </div></td></tr> : null}
      </tbody></table></div>
    </> : view === "analytics" ? <SupplierAssessmentAnalytics rows={rows} supplierName={supplierName}
      onCreate={canOperate ? () => { setSelectedId(0); setView("editor"); } : undefined} /> : <form className="form-grid" key={selected?.id ?? `new-${supplierId}`} onSubmit={save}>
      <div className="section-heading-row"><h4>{selected ? canOperate ? "编辑评价" : "查看评价" : "记录新评价"}</h4><button className="secondary-button" type="button" onClick={() => setView("directory")}>返回评价记录</button></div>
      <div className="form-field-wide context-strip"><strong>{supplierName}</strong><span>1 分表示明显不足，5 分表示表现优秀。</span></div>
      <fieldset className="permission-fieldset form-field-wide" disabled={!canOperate}>
      <label>评价日期<input name="assessedAt" type="date" required max={today} defaultValue={selected?.assessedAt.slice(0, 10) ?? today} /></label>
      <label>评价类型<select name="assessmentKind" defaultValue={selected?.assessmentKind ?? "定期评价"}><option>定期评价</option><option>订单复盘</option><option>样品评估</option><option>其它</option></select></label>
      <ScoreField name="qualityScore" label="质量评分" value={selected?.qualityScore ?? 4} />
      <ScoreField name="deliveryScore" label="交期评分" value={selected?.deliveryScore ?? 4} />
      <ScoreField name="serviceScore" label="服务评分" value={selected?.serviceScore ?? 4} />
      <ScoreField name="priceScore" label="价格评分" value={selected?.priceScore ?? 4} />
      <label>评价结论<select name="conclusion" defaultValue={selected?.conclusion ?? "合格"}><option>优先合作</option><option>合格</option><option>观察</option><option>暂停合作</option></select></label>
      <label className="form-field-wide">评价备注<textarea name="notes" maxLength={1000} defaultValue={selected?.notes} placeholder="记录事实、改进事项和下次复核重点" /></label>
      </fieldset>
      <div className="form-actions">{canOperate ? <button className="primary-button">保存评价</button> : null}{selected && canManage ? <button className="secondary-button danger-button" type="button" onClick={() => void remove()}>删除</button> : null}</div>
    </form>}
  </section>;
}

function ScoreField({ name, label, value }: { name: string; label: string; value: number }) {
  return <label>{label}<select name={name} defaultValue={String(value)}>
    <option value="1">1 - 明显不足</option><option value="2">2 - 需要改进</option><option value="3">3 - 基本符合</option>
    <option value="4">4 - 表现良好</option><option value="5">5 - 表现优秀</option>
  </select></label>;
}

function text(form: FormData, name: string) { return String(form.get(name) ?? "").trim(); }
function number(form: FormData, name: string) { return Number(form.get(name) ?? 0); }
