import { type ReactNode } from "react";
import {
SingleWindowOperationCenterPackageRecord,
SingleWindowOperationCenterReceiptRecord,
SingleWindowOperationTicketRow,
SingleWindowWorkstationRow
} from "../../api/index.ts";
import { formatPlainNumber } from "../../ui/formUtils.ts";

import {
formatBusinessType,
formatCollaborationStatus,
formatDateTime,
formatReceiptKind,
formatReceiptStatus,
formatWorkstationCapabilities,
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
    <div className="table-frame compact-table" aria-busy="false">
      <table className="single-window-package-table">
        <thead>
          <tr>
            <th>类型</th>
            <th>方向</th>
            <th>路径</th>
            <th className="amount-cell">负载</th>
            <th className="amount-cell">附件</th>
            <th className="amount-cell">警告</th>
            <th>机器</th>
            <th>创建</th>
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
              <tr key={`${record.filePath}-${index}`}>
                <td>{readDisplayText(record.packageType)}</td>
                <td>{readDisplayText(record.direction)}</td>
                <td className="path-cell" title={record.filePath}>
                  {readDisplayText(record.filePath)}
                </td>
                <td className="amount-cell">{formatPlainNumber(record.payloadFileCount ?? 0)}</td>
                <td className="amount-cell">{formatPlainNumber(record.attachmentFileCount ?? 0)}</td>
                <td className="amount-cell">{formatPlainNumber(record.warningCount ?? 0)}</td>
                <td>{readDisplayText(record.createdOnMachine)}</td>
                <td>{formatDateTime(record.createdAt)}</td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}

export function ReceiptRecordTable({ data }: { data: SingleWindowOperationCenterReceiptRecord[] }) {
  return (
    <div className="table-frame compact-table" aria-busy="false">
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
    </div>
  );
}

export function TicketTable({ data, isBusy }: { data: SingleWindowOperationTicketRow[]; isBusy: boolean }) {
  return (
    <div className="table-frame single-window-board-table" aria-busy={isBusy}>
      <table className="single-window-ticket-table">
        <thead>
          <tr>
            <th>工单</th>
            <th>业务</th>
            <th>状态</th>
            <th>优先级</th>
            <th>发票 ID</th>
            <th>文档 ID</th>
            <th>批次</th>
            <th>申请人</th>
            <th>操作员</th>
            <th>申请</th>
            <th>异常</th>
          </tr>
        </thead>
        <tbody>
          {data.length === 0 ? (
            <tr>
              <td colSpan={11} className="empty-cell">
                {isBusy ? "加载中" : "暂无数据"}
              </td>
            </tr>
          ) : (
            data.map((ticket) => (
              <tr key={ticket.ticketId}>
                <td className="strong-cell">{ticket.ticketId}</td>
                <td>{formatBusinessType(ticket.businessType)}</td>
                <td>
                  <span className="status-pill">{formatCollaborationStatus(ticket.status)}</span>
                </td>
                <td className="amount-cell">{formatPlainNumber(ticket.priority)}</td>
                <td>{ticket.sourceInvoiceId}</td>
                <td>{ticket.documentId}</td>
                <td>{ticket.batchId ?? "-"}</td>
                <td>{readDisplayText(ticket.requestedBy)}</td>
                <td>{readDisplayText(ticket.assignedOperator)}</td>
                <td>{formatDateTime(ticket.requestedAt)}</td>
                <td className="message-cell" title={ticket.lastError ?? ""}>
                  {readDisplayText(ticket.lastError)}
                </td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}

export function WorkstationTable({ data, isBusy }: { data: SingleWindowWorkstationRow[]; isBusy: boolean }) {
  return (
    <div className="table-frame single-window-board-table" aria-busy={isBusy}>
      <table className="single-window-workstation-table">
        <thead>
          <tr>
            <th>机器</th>
            <th>操作员</th>
            <th>配置 ID</th>
            <th>能力</th>
            <th>状态</th>
            <th>更新</th>
            <th>备注</th>
          </tr>
        </thead>
        <tbody>
          {data.length === 0 ? (
            <tr>
              <td colSpan={7} className="empty-cell">
                {isBusy ? "加载中" : "暂无数据"}
              </td>
            </tr>
          ) : (
            data.map((workstation) => (
              <tr key={workstation.workstationId}>
                <td className="strong-cell">{readDisplayText(workstation.machineName)}</td>
                <td>{readDisplayText(workstation.operatorName)}</td>
                <td>{workstation.profileId ?? "-"}</td>
                <td>{formatWorkstationCapabilities(workstation)}</td>
                <td>
                  <span className="status-pill">{workstation.isEnabled ? "启用" : "停用"}</span>
                </td>
                <td>{formatDateTime(workstation.updatedAt)}</td>
                <td className="message-cell" title={workstation.remarks ?? ""}>
                  {readDisplayText(workstation.remarks)}
                </td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}


