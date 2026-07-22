import type { ApiCustomsCooDocumentDto } from "../../api/index.ts";
import { formatDateTime, readDisplayText, readDisplayValue, shouldShowCooModificationFields, shouldShowCooNonpartyCorps } from "./customsCooModel.ts";

export function CooSummary({ document }: { document: ApiCustomsCooDocumentDto }) {
  const completion = buildCooCompletion(document);
  return (
    <>
      <div className="coo-completion-overview" aria-label={`草稿完成度 ${completion.percent}%`}>
        <div className="coo-completion-heading">
          <div><span>录入完成度</span><strong>{completion.percent}%</strong></div>
          <small>{completion.completedCount}/{completion.totalCount} 个关键项目已具备</small>
        </div>
        <div className="coo-completion-track" aria-hidden="true"><span style={{ width: `${completion.percent}%` }} /></div>
        <div className="coo-completion-steps">
          {completion.steps.map((step) => <span className={step.complete ? "complete" : "pending"} key={step.label}>{step.label}<b>{step.detail}</b></span>)}
        </div>
      </div>
      <div className="detail-grid customs-coo-summary-grid">
        <SummaryItem label="发票 ID" value={document.sourceInvoiceId} />
        <SummaryItem label="发票号" value={document.invNo || document.invoiceNo} />
        <SummaryItem label="状态" value={document.status} />
        <SummaryItem label="草稿版本" value={document.draftRevision} />
        <SummaryItem label="人工锁定字段" value={document.manualLockedFieldCount} />
        <SummaryItem label="来源差异" value={document.sourceDiffCount} />
        <SummaryItem label="预警" value={document.warningCount} />
        <SummaryItem label="最后生成" value={formatDateTime(document.lastGeneratedAt)} />
        <SummaryItem label="来源差异摘要" value={document.sourceDiffSummary} wide />
        <SummaryItem label="预警摘要" value={document.warningSummary} wide />
      </div>
    </>
  );
}

function buildCooCompletion(document: ApiCustomsCooDocumentDto) {
  const hasText = (value?: string) => Boolean(value?.trim());
  const steps = [
    { label: "证书基础", complete: hasText(document.certType) && hasText(document.etpsName) && hasText(document.aplRegNo), detail: hasText(document.certType) && hasText(document.etpsName) && hasText(document.aplRegNo) ? "已具备" : "待补充" },
    { label: "申报对象", complete: hasText(document.exporter) && hasText(document.consignee) && hasText(document.destCountry), detail: hasText(document.exporter) && hasText(document.consignee) && hasText(document.destCountry) ? "已具备" : "待补充" },
    { label: "运输贸易", complete: hasText(document.transMeans) && hasText(document.destPort) && hasText(document.curr), detail: hasText(document.transMeans) && hasText(document.destPort) && hasText(document.curr) ? "已具备" : "待补充" },
    { label: "商品明细", complete: document.items.length > 0, detail: `${document.items.length} 行` },
    { label: "附件", complete: document.attachments.length > 0, detail: `${document.attachments.length} 条` },
    { label: "预警", complete: document.warningCount === 0, detail: document.warningCount === 0 ? "无预警" : `${document.warningCount} 条` },
  ];
  const completedCount = steps.filter((step) => step.complete).length;
  return { steps, completedCount, totalCount: steps.length, percent: Math.round(completedCount / steps.length * 100) };
}

export function buildCustomsCooSectionNavItems(document: ApiCustomsCooDocumentDto) {
  return [
    { id: "coo-section-status", label: "草稿", badge: `${document.warningCount} 预警` },
    { id: "coo-section-basic", label: "证书基础" },
    { id: "coo-section-parties", label: "申报对象" },
    { id: "coo-section-trade", label: "运输贸易" },
    { id: "coo-section-special", label: "补充项" },
    ...(shouldShowCooModificationFields(document) ? [{ id: "coo-section-modification", label: "更改重发" }] : []),
    { id: "coo-section-items", label: "商品", badge: `${document.items.length} 行` },
    ...(shouldShowCooNonpartyCorps(document) ? [{ id: "coo-section-nonparty", label: "第三方", badge: `${document.nonpartyCorps.length} 条` }] : []),
    { id: "coo-section-attachments", label: "附件", badge: `${document.attachments.length} 条` },
    { id: "coo-section-review", label: "预检" },
  ];
}

function SummaryItem({ label, value, wide }: { label: string; value?: string | number; wide?: boolean }) {
  const displayValue = readDisplayValue(value);

  return (
    <div className={wide ? "detail-item detail-item-wide" : "detail-item"}>
      <span>{label}</span>
      <strong title={displayValue}>{displayValue}</strong>
    </div>
  );
}
