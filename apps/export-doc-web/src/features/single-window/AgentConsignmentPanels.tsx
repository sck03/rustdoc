import { type ReactNode, useId } from "react";
import type { ApiAgentConsignmentDocumentDto } from "../../api/index.ts";
import { FieldShell, SelectField, TextAreaField, TextField } from "../../ui/FormFields.tsx";
import { formatPlainNumber } from "../../ui/formUtils.ts";
import {
  buildAgentConsignmentEditorOptions,
  formatAgentDateTime,
  readAgentDisplayText,
  readAgentDisplayValue,
} from "./agentConsignmentModel.ts";

const agentOperTypeOptions = [
  { value: "1", label: "1：新增" },
  { value: "2", label: "2：变更" },
  { value: "3", label: "3：删除" },
];
const agentPackingConditionOptions = ["纸箱", "托盘", "木箱", "裸装", "散装", "其他包装"];
const agentPaperInfoOptions = ["已收齐", "待补充", "发票", "装箱单", "合同", "提单", "报关委托书", "海关原产地证", "其他"];

export function AgentConsignmentSummary({ document }: { document: ApiAgentConsignmentDocumentDto }) {
  return (
    <div className="detail-grid single-window-document-summary-grid">
      <SummaryItem label="发票 ID" value={document.sourceInvoiceId} />
      <SummaryItem label="发票号" value={document.invoiceNo} />
      <SummaryItem label="合同号" value={document.contractNo} />
      <SummaryItem label="状态" value={document.status} />
      <SummaryItem label="委托编号" value={document.consignNo} />
      <SummaryItem label="对方状态" value={document.counterpartyStatus} />
      <SummaryItem label="人工锁定字段" value={document.manualLockedFieldCount} />
      <SummaryItem label="来源差异" value={document.sourceDiffCount} />
      <SummaryItem label="预警" value={document.warningCount} />
      <SummaryItem label="最后生成" value={formatAgentDateTime(document.lastGeneratedAt)} />
      <SummaryItem label="来源差异摘要" value={document.sourceDiffSummary} wide />
      <SummaryItem label="预警摘要" value={document.warningSummary} wide />
    </div>
  );
}

export function AgentConsignmentWorkbench({
  document,
  editorOptions,
  onPatchDocument,
}: {
  document: ApiAgentConsignmentDocumentDto;
  editorOptions: ReturnType<typeof buildAgentConsignmentEditorOptions>;
  onPatchDocument: (next: Partial<ApiAgentConsignmentDocumentDto>) => void;
}) {
  return (
    <div className="agent-consignment-workbench">
      <div className="agent-consignment-workbench-main">
        <AgentConsignmentCard
          title="企业与操作资料"
          meta="企业编码、操作类型和签名会进入导入请求的操作段。"
        >
          <div className="field-grid agent-consignment-compact-grid">
            <TextField
              label="企业内部编号"
              value={document.copCusCode}
              required
              description="10 位企业海关编码，通常与经营单位编码一致。"
              onChange={(value) => onPatchDocument({ copCusCode: value })}
            />
            <SelectField
              label="操作类型"
              value={document.operType}
              options={agentOperTypeOptions}
              description="常规新增委托使用 1。"
              onChange={(value) => onPatchDocument({ operType: value })}
            />
            <TextField
              label="数字签名"
              value={document.sign}
              description="正式导入时由官方签名机制处理，可先留空。"
              onChange={(value) => onPatchDocument({ sign: value })}
            />
          </div>
        </AgentConsignmentCard>

        <AgentConsignmentCard
          title="核心申报信息"
          meta="优先复核必填字段，决定交接包能否顺利导入。"
        >
          <div className="field-grid agent-consignment-critical-grid">
            <TextField
              label="主要货物名称"
              value={document.gName}
              required
              description="默认取首项商品中文品名，必要时可改为业务概括。"
              onChange={(value) => onPatchDocument({ gName: value })}
            />
            <TextField
              label="HS编码"
              value={document.codeTS}
              required
              description="10 位以内 HS 编码。"
              onChange={(value) => onPatchDocument({ codeTS: value })}
            />
            <TextField
              label="货物总价"
              value={document.declTotal}
              required
              description="最多 4 位小数。"
              onChange={(value) => onPatchDocument({ declTotal: value })}
            />
            <TextField
              label="进出口日期"
              value={document.ieDate}
              required
              description="格式 yyyyMMdd，例如 20260417。"
              onChange={(value) => onPatchDocument({ ieDate: value })}
            />
            <AgentCandidateField
              label="贸易方式"
              value={document.tradeMode}
              required
              options={editorOptions.tradeModeOptions}
              description="使用 ACD 监管方式代码，例如一般贸易 0110。"
              onChange={(value) => onPatchDocument({ tradeMode: value })}
            />
            <AgentCandidateField
              label="原产地/货源地"
              value={document.oriCountry}
              required
              options={editorOptions.countryOptions}
              description="使用海关 GBDQ 代码，例如中国 142。"
              onChange={(value) => onPatchDocument({ oriCountry: value })}
            />
            <TextField
              label="经营单位(委托方)海关10位编码"
              value={document.tradeCode}
              required
              onChange={(value) => onPatchDocument({ tradeCode: value })}
            />
            <TextField
              label="申报单位(被委托方)海关10位编码"
              value={document.agentCode}
              required
              onChange={(value) => onPatchDocument({ agentCode: value })}
            />
          </div>
        </AgentConsignmentCard>

        <AgentConsignmentCard title="辅助申报" meta="非必填但常用于现场导入和人工复核。">
          <div className="field-grid agent-consignment-compact-grid">
            <TextField label="提单号" value={document.listNo} onChange={(value) => onPatchDocument({ listNo: value })} />
            <AgentCandidateField
              label="币制代码"
              value={document.curr}
              options={editorOptions.currencyOptions}
              description="使用 ACD 海关币制码，例如 USD 为 502。"
              onChange={(value) => onPatchDocument({ curr: value })}
            />
            <TextField
              label="数量/重量"
              value={document.qtyOrWeight}
              onChange={(value) => onPatchDocument({ qtyOrWeight: value })}
            />
            <AgentCandidateField
              label="包装情况"
              value={document.packingCondition}
              options={agentPackingConditionOptions.map((value) => ({ value, label: value }))}
              onChange={(value) => onPatchDocument({ packingCondition: value })}
            />
            <TextAreaField
              label="其他要求"
              value={document.otherNote}
              className="agent-consignment-wide-text"
              onChange={(value) => onPatchDocument({ otherNote: value })}
            />
          </div>
        </AgentConsignmentCard>
      </div>
    </div>
  );
}

export function AgentConsignmentDocumentsPanel({
  document,
  onPatchDocument,
}: {
  document: ApiAgentConsignmentDocumentDto;
  onPatchDocument: (next: Partial<ApiAgentConsignmentDocumentDto>) => void;
}) {
  return (
    <div className="agent-consignment-documents-grid">
      <AgentConsignmentCard title="联系与收件" meta="用于委托双方联系、单证交接和后续补充。">
        <div className="field-grid agent-consignment-compact-grid">
          <TextField
            label="委托方电话"
            value={document.consignTele}
            onChange={(value) => onPatchDocument({ consignTele: value })}
          />
          <TextField
            label="被委托方电话"
            value={document.declTele}
            onChange={(value) => onPatchDocument({ declTele: value })}
          />
          <TextField
            label="收到证件日期"
            value={document.receiveDate}
            description="格式 yyyyMMdd。"
            onChange={(value) => onPatchDocument({ receiveDate: value })}
          />
          <AgentCandidateField
            label="收到单证情况"
            value={document.paperInfo}
            options={agentPaperInfoOptions.map((value) => ({ value, label: value }))}
            onChange={(value) => onPatchDocument({ paperInfo: value })}
          />
          <TextAreaField
            label="其他收件信息"
            value={document.otherRecInfo}
            className="agent-consignment-wide-text"
            onChange={(value) => onPatchDocument({ otherRecInfo: value })}
          />
        </div>
      </AgentConsignmentCard>

      <AgentConsignmentCard title="单证与费用" meta="报关单号、收费和承诺说明可在确认后补录。">
        <div className="field-grid agent-consignment-compact-grid">
          <TextField
            label="报关单编号"
            value={document.entryId}
            onChange={(value) => onPatchDocument({ entryId: value })}
          />
          <TextField
            label="报关收费"
            value={document.declarePrice}
            description="人民币金额，最多 2 位小数。"
            onChange={(value) => onPatchDocument({ declarePrice: value })}
          />
          <TextAreaField
            label="承诺说明"
            value={document.promiseNote}
            className="agent-consignment-wide-text"
            onChange={(value) => onPatchDocument({ promiseNote: value })}
          />
        </div>
      </AgentConsignmentCard>
    </div>
  );
}

export function AgentConsignmentReceiptPanel({ document }: { document: ApiAgentConsignmentDocumentDto }) {
  return (
    <div className="agent-consignment-receipt-grid">
      <div className="agent-consignment-receipt-card">
        <span>委托编号</span>
        <strong>{readAgentDisplayText(document.consignNo)}</strong>
      </div>
      <div className="agent-consignment-receipt-card">
        <span>对方状态</span>
        <strong>{readAgentDisplayText(document.counterpartyStatus)}</strong>
      </div>
    </div>
  );
}

function AgentConsignmentCard({
  title,
  meta,
  children,
}: {
  title: string;
  meta?: string;
  children: ReactNode;
}) {
  return (
    <section className="agent-consignment-card workspace-surface-card">
      <div className="agent-consignment-card-header">
        <h3>{title}</h3>
        {meta ? <span>{meta}</span> : null}
      </div>
      {children}
    </section>
  );
}

function SummaryItem({ label, value, wide }: { label: string; value?: string | number; wide?: boolean }) {
  const displayValue = readAgentDisplayValue(value);

  return (
    <div className={wide ? "detail-item detail-item-wide" : "detail-item"}>
      <span>{label}</span>
      <strong title={displayValue}>{displayValue}</strong>
    </div>
  );
}

function AgentCandidateField({
  label,
  value,
  required,
  disabled,
  description,
  options,
  onChange,
}: {
  label: string;
  value?: string;
  required?: boolean;
  disabled?: boolean;
  description?: string;
  options: Array<{ value: string; label: string }>;
  onChange: (value: string) => void;
}) {
  const listId = `agent-candidate-${useId().replace(/:/g, "-")}`;
  const normalizedOptions = options.filter((option) => option.value.trim());

  return (
    <FieldShell label={label} required={required} disabled={disabled} description={description}>
      {(descriptionId) => (
        <>
          <input
            list={normalizedOptions.length > 0 ? listId : undefined}
            value={value ?? ""}
            required={required}
            disabled={disabled}
            aria-describedby={descriptionId}
            onChange={(event) => onChange(event.target.value)}
          />
          {normalizedOptions.length > 0 ? (
            <datalist id={listId}>
              {normalizedOptions.map((option) => (
                <option key={option.value} value={option.value} label={option.label} />
              ))}
            </datalist>
          ) : null}
        </>
      )}
    </FieldShell>
  );
}
