import { ClipboardList, Copy, FileText, RotateCcw, Save } from "lucide-react";
import type { ApiInvoiceDetailDto, ApiInvoiceStatusHistoryDto } from "../../api/index.ts";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import { DateField, EditableComboField, NumberField, SelectField, TextField } from "../../ui/FormFields.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { getCustomOptions, type CustomOptionMap } from "../custom-options/customOptionModel.ts";
import {
  getInvoiceStatusActionLabel,
  getInvoiceStatusLabel,
  invoiceTypeOptions,
  normalizeInvoiceType,
} from "./invoiceModel.ts";

type InvoicePatch = Partial<ApiInvoiceDetailDto>;

export function InvoiceBasicInfoPanel({
  invoice,
  canOpenSingleWindowDocuments,
  canCloneInvoiceType,
  cloneInvoiceTypeLabel,
  canUnverifyInvoice,
  canTransitionStatus,
  canCancelStatus,
  isEditable,
  isBusy,
  isCloneInvoiceTypeBusy,
  isUnverifyInvoiceBusy,
  isTransitionStatusBusy,
  onTransitionStatus,
  onCancelStatus,
  statusHistory,
  statusHistoryLoading,
  statusHistoryMessage,
  onChange,
  onCloneInvoiceType,
  onUnverifyInvoice,
  onOpenCustomsCoo,
  onOpenAgentConsignment,
  customOptions,
  onCommitCustomOption,
}: {
  invoice: ApiInvoiceDetailDto;
  canOpenSingleWindowDocuments: boolean;
  canCloneInvoiceType: boolean;
  cloneInvoiceTypeLabel: string;
  canUnverifyInvoice: boolean;
  canTransitionStatus: boolean;
  canCancelStatus: boolean;
  isEditable: boolean;
  isBusy: boolean;
  isCloneInvoiceTypeBusy: boolean;
  isUnverifyInvoiceBusy: boolean;
  isTransitionStatusBusy: boolean;
  onChange: (next: InvoicePatch) => void;
  onCloneInvoiceType: () => void;
  onUnverifyInvoice: () => void;
  onTransitionStatus: () => void;
  onCancelStatus: () => void;
  onOpenCustomsCoo: () => void;
  onOpenAgentConsignment: () => void;
  statusHistory?: ApiInvoiceStatusHistoryDto[];
  statusHistoryLoading?: boolean;
  statusHistoryMessage?: string | null;
  customOptions?: CustomOptionMap;
  onCommitCustomOption?: (optionType: string, value: string) => void;
}) {
  return (
    <section className="form-section information-tier-required" aria-label="基础信息">
      <div className="section-header">
        <h2>基础信息</h2>
        <div className="toolbar-actions">
          {canOpenSingleWindowDocuments ? (
            <>
              <button className="command-button secondary" type="button" onClick={onOpenCustomsCoo}>
                <FileText size={17} aria-hidden="true" />
                <span>海关原产地证</span>
              </button>
              <button className="command-button secondary" type="button" onClick={onOpenAgentConsignment}>
                <ClipboardList size={17} aria-hidden="true" />
                <span>代理委托</span>
              </button>
            </>
          ) : null}
          {canCloneInvoiceType ? (
            <button
              className="command-button secondary"
              type="button"
              disabled={isBusy || isCloneInvoiceTypeBusy}
              onClick={onCloneInvoiceType}
            >
              <Copy size={17} aria-hidden="true" />
              <span>{cloneInvoiceTypeLabel}</span>
            </button>
          ) : null}
          {canUnverifyInvoice ? (
            <button
              className="command-button secondary"
              type="button"
              disabled={isBusy || isUnverifyInvoiceBusy}
              onClick={onUnverifyInvoice}
            >
              <RotateCcw size={17} aria-hidden="true" />
              <span>反审核</span>
            </button>
          ) : null}
          {canTransitionStatus ? (
            <button
              className="command-button secondary"
              type="button"
              disabled={isBusy || isTransitionStatusBusy}
              onClick={onTransitionStatus}
            >
              <span>{getInvoiceStatusActionLabel(invoice.status)}</span>
            </button>
          ) : null}
          {canCancelStatus ? (
            <button
              className="command-button danger"
              type="button"
              disabled={isBusy || isTransitionStatusBusy}
              onClick={onCancelStatus}
            >
              <span>作废</span>
            </button>
          ) : null}
          <button className="command-button" type="submit" disabled={isBusy || !isEditable}>
            <Save size={17} aria-hidden="true" />
            <span>保存</span>
          </button>
        </div>
      </div>
      <div className="field-grid">
        <TextField label="发票号" value={invoice.invoiceNo} required disabled={!isEditable} onChange={(value) => onChange({ invoiceNo: value })} />
        <TextField label="合同号" value={invoice.contractNo} disabled={!isEditable} onChange={(value) => onChange({ contractNo: value })} />
        <DateField label="发票日期" value={invoice.invoiceDate} disabled={!isEditable} onChange={(value) => onChange({ invoiceDate: value })} />
        <DateField label="出运日期" value={invoice.shipmentDate} disabled={!isEditable} onChange={(value) => onChange({ shipmentDate: value })} />
        <EditableComboField
          label="币种"
          value={invoice.currency}
          disabled={!isEditable}
          options={getCustomOptions(customOptions, "Currency")}
          transformValue={(value) => value.toUpperCase()}
          onChange={(value) => onChange({ currency: value })}
          onCommit={(value) => onCommitCustomOption?.("Currency", value)}
        />
        <EditableComboField
          label="监管方式"
          value={invoice.supervisionMode ?? ""}
          disabled={!isEditable}
          options={getCustomOptions(customOptions, "SupervisionMode")}
          onChange={(value) => onChange({ supervisionMode: value })}
          onCommit={(value) => onCommitCustomOption?.("SupervisionMode", value)}
        />
        <div className="form-field form-field-disabled invoice-status-field" aria-label="状态">
          <span className="form-field-label"><span>状态</span></span>
          <div className="invoice-status-field-value">
            <BusinessStatusBadge value={getInvoiceStatusLabel(invoice.status)} />
            <small>通过状态操作推进，不能直接编辑</small>
          </div>
        </div>
        <SelectField
          label="业务类型"
          value={normalizeInvoiceType(invoice.type)}
          disabled={!isEditable}
          includeEmptyOption={false}
          options={invoiceTypeOptions}
          onChange={(value) => onChange({ type: normalizeInvoiceType(value) })}
        />
        <NumberField
          label="总金额"
          value={invoice.totalAmount}
          disabled
          description="由商品明细行金额合计；修改单价或行金额会自动联动"
          onChange={() => undefined}
        />
      </div>
      {statusHistory !== undefined || statusHistoryLoading || statusHistoryMessage ? (
        <details className="invoice-status-history">
          <summary>状态记录{statusHistory?.length ? `（${statusHistory.length}）` : ""}</summary>
          {statusHistoryLoading ? <p className="form-field-description">正在加载状态记录…</p> : null}
          {statusHistoryMessage ? <InlineNotice tone="warning">{statusHistoryMessage}</InlineNotice> : null}
          {!statusHistoryLoading && !statusHistoryMessage && !(statusHistory?.length) ? (
            <p className="form-field-description">暂时没有状态变更记录。</p>
          ) : null}
          {statusHistory?.length ? (
            <ol className="invoice-status-history-list">
              {statusHistory.map((entry) => (
                <li key={entry.id}>
                  <div>
                    <strong>{getInvoiceStatusLabel(entry.fromStatus)} → {getInvoiceStatusLabel(entry.toStatus)}</strong>
                    <span>{entry.note || "未填写备注"}</span>
                  </div>
                  <small>{entry.changedByUsername || "系统"} · {new Date(entry.changedAt).toLocaleString("zh-CN", { hour12: false })}</small>
                </li>
              ))}
            </ol>
          ) : null}
        </details>
      ) : null}
    </section>
  );
}
