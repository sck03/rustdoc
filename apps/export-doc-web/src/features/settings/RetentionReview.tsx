import { useState } from "react";
import type { ApiRetentionPlan } from "../../api/index.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { Button } from "../../ui/Button.tsx";
import { formatBytes, formatRuntimeDate } from "./settingsFormatters.ts";

export function RetentionReview({ plan, disabled, approve }: { plan: ApiRetentionPlan; disabled: boolean; approve: () => void }) {
  const [reviewed, setReviewed] = useState(false);
  return <section className="dashboard-section-stack" aria-label="档案处置复核清单">
    <InlineNotice tone="warning">待批准 {plan.count} 个归档引用，共 {formatBytes(plan.bytes)}；申请账号 #{plan.requestedBy}；预检有效至 {formatRuntimeDate(plan.expiresAt)}。</InlineNotice>
    <p>处置理由：{plan.reason}</p>
    <ResponsiveTableFrame label="待处置档案" className="summary-table-frame">
      <table className="data-table" aria-label="待处置档案"><thead><tr><th>文件</th><th>单位</th><th>类别</th><th>归档日期</th><th>到期日期</th><th>容量</th></tr></thead>
        <tbody>{plan.items.map(row => <tr key={row.id}><td>{row.fileName}</td><td>{row.company}</td><td>{row.archiveClass}</td><td>{row.basisDate}</td><td>{row.expiresOn}</td><td>{formatBytes(row.size)}</td></tr>)}</tbody>
      </table>
    </ResponsiveTableFrame>
    <label className="checkbox-field"><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} /><span>已核对处置理由及全部文件明细</span></label>
    <Button disabled={disabled || !reviewed} onClick={approve}>独立复核并批准</Button>
  </section>;
}
