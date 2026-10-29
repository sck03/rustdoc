import { useEffect, useState } from "react";
import type { ExportDocManagerApiClient, OaApprovalSettings, OaSheetTemplate } from "../../api/index.ts";
import { OfficeDialog, OfficeField } from "../office/OfficeUi.tsx";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import { useReportTemplateDocument } from "../reports/useReportTemplateDocument.ts";
import { ReportDesignerV3Workspace } from "../report-designer/ReportDesignerV3Workspace.tsx";
import "../../styles/routes/reports.css";

export function ApprovalSheetTemplates({ client, settings, disabled, onChange }: {
  client: ExportDocManagerApiClient; settings: OaApprovalSettings; disabled: boolean; onChange: (templates: OaSheetTemplate[]) => void;
}) {
  const [selected, setSelected] = useState<OaSheetTemplate | null>(null);
  function restoreDefault(key?: string) {
    if (!settings.sheetDefaultContent) return;
    onChange((settings.sheetTemplates ?? []).map(item => key && item.key !== key ? item : { ...item, content: settings.sheetDefaultContent! }));
  }
  function applyMonochrome(key: string) {
    onChange((settings.sheetTemplates ?? []).map(item => item.key !== key ? item : {
      ...item,
      content: JSON.stringify(JSON.parse(item.content, (property, value) =>
        property === "backgroundColor" ? "#ffffff" : property === "color" || property === "borderColor" ? "#000000" : value)),
    }));
  }
  return <details><summary>审批单名称与打印模板</summary>
    <p className="office-muted">默认 A4 白底黑字，按审批节点显示审批人、处理结果、意见及时间，长流程自动续页。恢复默认版式保留表单名称；修改先进入草稿，保存后用于后续提交版本。</p>
    <button className="command-button secondary" type="button" disabled={disabled || !settings.sheetDefaultContent} onClick={() => restoreDefault()}>全部恢复默认版式</button>
    <ul>{settings.sheetTemplates?.map(template => <li key={template.key}>{template.name} <button className="command-button secondary" type="button" disabled={disabled} onClick={() => setSelected(template)}>编辑审批单模板</button> <button className="command-button secondary" type="button" disabled={disabled} onClick={() => applyMonochrome(template.key)}>使用黑白打印样式</button> <button className="command-button secondary" type="button" disabled={disabled || !settings.sheetDefaultContent} onClick={() => restoreDefault(template.key)}>恢复默认版式</button></li>)}</ul>
    {selected && <SheetEditor key={selected.key} client={client} template={selected} catalog={settings.sheetFieldCatalog} onClose={() => setSelected(null)} onApply={template => {
      onChange((settings.sheetTemplates ?? []).map(item => item.key === template.key ? template : item)); setSelected(null);
    }} />}
  </details>;
}
function SheetEditor({ client, template, catalog, onClose, onApply }: {
  client: ExportDocManagerApiClient; template: OaSheetTemplate; catalog: OaApprovalSettings["sheetFieldCatalog"];
  onClose: () => void; onApply: (template: OaSheetTemplate) => void;
}) {
  const model = useReportTemplateDocument();
  const { loadFile } = model;
  const operation = useOfficeOperation([], false);
  const [html, setHtml] = useState("");
  useEffect(() => { loadFile({ templatePath: template.key, displayName: template.name, content: template.content, revision: "", reportType: "OaApproval", storagePolicy: "Company" }); }, [template, loadFile]);
  function validate(apply: boolean) {
    const content = model.readContent();
    if (content === null) return;
    const value = { key: template.key, name: model.document.name.trim(), content };
    void operation.run(signal => client.previewOaApprovalSheet({ body: value }, { signal }), result => {
      if (apply) onApply(value); else setHtml(result.html);
    }, false);
  }
  return <OfficeDialog title="编辑审批单模板" onClose={onClose} busy={operation.busy} error={operation.error} protectChanges hasChanges={model.hasUnsavedChanges} wide>
    <OfficeField label="审批单名称"><input maxLength={80} required value={model.document.name} disabled={operation.busy} onChange={event => model.setName(event.target.value)} /></OfficeField>
    <div className="office-card-actions"><button type="button" disabled={operation.busy || !model.document.name.trim()} onClick={() => validate(false)}>预览审批单样例</button>
      <button type="button" disabled={operation.busy || !model.document.name.trim()} onClick={() => validate(true)}>应用到设置草稿</button></div>
    <p className="office-muted">应用后还须保存审批设置。请将申请摘要、说明、附件及审批记录放入正文的自动增高表格，保证长内容完整输出。</p>
    {model.document.path && <ReportDesignerV3Workspace reportType="OaApproval" displayName={model.document.name} content={model.document.content} fieldCatalog={catalog} editable={!operation.busy} onDesignerDraftChange={model.setDraft} />}
    {html && <OfficeDialog title="审批单样例预览" onClose={() => setHtml("")}><iframe title="审批单样例" sandbox="" srcDoc={html} style={{width:"100%",height:"75vh",border:0}} /></OfficeDialog>}
  </OfficeDialog>;
}
