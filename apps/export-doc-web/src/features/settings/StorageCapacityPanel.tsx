import type { Dispatch, SetStateAction } from "react";
import type { ApiCapacityPolicy, ApiStorageCapacityStatus } from "../../api/index.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { Button } from "../../ui/Button.tsx";
import { NumberField } from "../../ui/FormFields.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { CollapsibleSection } from "../../ui/primitives/Card.tsx";
import { formatBytes, formatRuntimeDate } from "./settingsFormatters.ts";

const stateLabels: Record<string, string> = { Normal: "正常", Warning: "预警", Critical: "容量不足", Unavailable: "不可用", Unknown: "未知" };
type Draft = { version: number; value: ApiCapacityPolicy } | null;
export function StorageCapacityPanel({ capacity, policy, setPolicy, savePolicy, disabled }: {
  capacity: ApiStorageCapacityStatus | undefined;
  policy: Draft;
  setPolicy: Dispatch<SetStateAction<Draft>>;
  savePolicy: () => Promise<void>;
  disabled: boolean;
}) {
  return (
    <CollapsibleSection title="容量趋势与告警" defaultOpen>
      {capacity && <>
        <ResponsiveTableFrame label="容量区域统计" className="summary-table-frame">
          <table className="data-table" aria-label="容量区域统计">
          <thead>
          <tr>
          <th>区域</th>
          <th>物理占用</th>
          <th>引用容量</th>
          <th>剩余容量</th>
          <th>状态</th>
          </tr>
          </thead>
          <tbody>{capacity.areas.map(area => <tr key={area.id}>
          <td>{area.name}</td>
          <td>{area.physicalBytes === undefined ? "未知" : formatBytes(area.physicalBytes)}</td>
          <td>{area.logicalBytes === undefined ? "—" : formatBytes(area.logicalBytes)}</td>
          <td>{area.availableBytes === undefined ? "未知" : formatBytes(area.availableBytes)}</td>
          <td title={area.message}>{stateLabels[area.state] ?? area.state}</td>
          </tr>)}</tbody>
          </table>
          </ResponsiveTableFrame>
        {capacity.areas.filter(area => ["Warning", "Critical", "Unavailable"].includes(area.state)).map(area => <InlineNotice key={area.id} tone="warning">{area.name}：{area.message}</InlineNotice>)}
        <p className="settings-helper-text">最近采样：{formatRuntimeDate(capacity.updatedAt)} · 正式档案受保护容量：{formatBytes(capacity.areas.find(a => a.id === "Files")?.protectedBytes ?? 0)}</p>
        <CollapsibleSection title="日汇总与阈值">
          <div className="field-grid">{([["warningPercent", "预警占用（%）"], ["criticalPercent", "严重占用（%）"], ["growthDays", "增长观察期（天）"]] as const).map(([key, label]) => <NumberField key={key} label={label} value={(policy?.value ?? capacity.policy)[key]} onChange={value => setPolicy(current => ({ version: current?.version ?? capacity.revision, value: { ...(current?.value ?? capacity.policy), [key]: value } }))} />)}
            <NumberField label="最少空闲容量（MiB）" value={(policy?.value ?? capacity.policy).minimumFreeBytes / 1048576} onChange={value => setPolicy(current => ({ version: current?.version ?? capacity.revision, value: { ...(current?.value ?? capacity.policy), minimumFreeBytes: Math.round(value * 1048576) } }))} />
          </div>
          <Button disabled={disabled || !policy} onClick={() => void savePolicy()}>保存告警策略</Button>
          <ResponsiveTableFrame label="容量日汇总" className="summary-table-frame">
            <table className="data-table" aria-label="容量日汇总">
            <thead>
            <tr>
            <th>日期</th>
            <th>位置</th>
            <th>物理占用</th>
            <th>可回收</th>
            </tr>
            </thead>
            <tbody>{capacity.samples.map(sample => <tr key={`${sample.day}:${sample.location}`}>
            <td>{sample.day}</td>
            <td>{sample.location === "Files" ? "生产文件库" : sample.location}</td>
            <td>{formatBytes(sample.physicalBytes)}</td>
            <td>{formatBytes(sample.reclaimableBytes)}</td>
            </tr>)}</tbody>
            </table>
            </ResponsiveTableFrame>
        </CollapsibleSection>
        <CollapsibleSection title={`历史告警（${capacity.alerts.length}）`}>
          {capacity.alerts.length ? <ResponsiveTableFrame label="容量历史告警" className="summary-table-frame">
            <table className="data-table" aria-label="容量历史告警">
            <thead>
            <tr>
            <th>时间</th>
            <th>区域</th>
            <th>状态</th>
            <th>原因与处理</th>
            </tr>
            </thead>
            <tbody>{capacity.alerts.slice().reverse().map((alert, index) => <tr key={`${alert.at}:${alert.area}:${index}`}>
            <td>{formatRuntimeDate(alert.at)}</td>
            <td>{capacity.areas.find(area => area.id === alert.area)?.name ?? alert.area}</td>
            <td>{stateLabels[alert.state] ?? alert.state}</td>
            <td>{alert.message}</td>
            </tr>)}</tbody>
            </table>
            </ResponsiveTableFrame> : <p className="settings-helper-text">暂无容量告警。</p>}
        </CollapsibleSection>
      </>}
    </CollapsibleSection>
  );
}
