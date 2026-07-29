import { type ReactNode } from "react";
import {
SingleWindowOperationCenterPackageRecord,
SingleWindowOperationCenterReceiptRecord
} from "../../api/index.ts";
import { formatPlainNumber } from "../../ui/formUtils.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";

import {
formatDateTime,
formatReceiptKind,
formatReceiptStatus,
readDisplayText,
readDisplayValue
} from "./singleWindowOperationCenterModel.ts";


export function DetailItem({
  label,
  value,
  wide,
  actions,
}: {
  label: string;
  value?: string | number;
  wide?: boolean;
  actions?: ReactNode;
}) {
  const displayValue = readDisplayValue(value);

  return (
    <div className={wide ? "detail-item detail-item-wide" : "detail-item"}>
      <span>{label}</span>
      {actions ? (
        <div className="detail-value-row">
          <strong title={displayValue}>{displayValue}</strong>
          <div className="detail-item-actions">{actions}</div>
        </div>
      ) : (
        <strong title={displayValue}>{displayValue}</strong>
      )}
    </div>
  );
}

export function PackageRecordTable({ data }: { data: SingleWindowOperationCenterPackageRecord[] }) {
  return (
    <ResponsiveTableFrame label="单一窗口包记录" className="compact-table" mobileLayout="scroll">
      <table className="single-window-package-table">
        <thead>
          <tr>
            <th>类型</th>
            <th>方向</th>
            <th className="amount-cell">负载</th>
            <th className="amount-cell">附件</th>
            <th className="amount-cell">警告</th>
            <th>创建</th>
          </tr>
        </thead>
        <tbody>
          {data.length === 0 ? (
            <tr>
              <td colSpan={6} className="empty-cell small-empty">
                暂无数据
              </td>
            </tr>
          ) : (
            data.map((record, index) => (
              <tr key={`${record.packageType}-${record.direction}-${record.createdAt}-${index}`}>
                <td>{readDisplayText(record.packageType)}</td>
                <td>{readDisplayText(record.direction)}</td>
                <td className="amount-cell">{formatPlainNumber(record.payloadFileCount ?? 0)}</td>
                <td className="amount-cell">{formatPlainNumber(record.attachmentFileCount ?? 0)}</td>
                <td className="amount-cell">{formatPlainNumber(record.warningCount ?? 0)}</td>
                <td>{formatDateTime(record.createdAt)}</td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </ResponsiveTableFrame>
  );
}

export function ReceiptRecordTable({ data }: { data: SingleWindowOperationCenterReceiptRecord[] }) {
  return (
    <ResponsiveTableFrame label="单一窗口回执记录" className="compact-table" mobileLayout="scroll">
      <table className="single-window-receipt-table">
        <thead>
          <tr>
            <th>类型</th>
            <th>状态</th>
            <th>回执码</th>
            <th>消息</th>
            <th>参考号</th>
            <th>来源文件</th>
            <th>发生</th>
            <th>导入</th>
          </tr>
        </thead>
        <tbody>
          {data.length === 0 ? (
            <tr>
              <td colSpan={8} className="empty-cell small-empty">
                暂无数据
              </td>
            </tr>
          ) : (
            data.map((record, index) => (
              <tr key={`${record.sourceFileName}-${record.importedAt}-${index}`}>
                <td>{formatReceiptKind(record.receiptKind)}</td>
                <td>
                  <span className="status-pill">{formatReceiptStatus(record.businessStatus)}</span>
                </td>
                <td>{readDisplayText(record.receiptCode)}</td>
                <td className="message-cell" title={record.receiptMessage ?? ""}>
                  {readDisplayText(record.receiptMessage)}
                </td>
                <td>{readDisplayText(record.referenceNo)}</td>
                <td className="path-cell" title={record.sourceFileName}>
                  {readDisplayText(record.sourceFileName)}
                </td>
                <td>{formatDateTime(record.occurredAt)}</td>
                <td>{formatDateTime(record.importedAt)}</td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </ResponsiveTableFrame>
  );
}
