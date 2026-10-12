import { useState } from "react";
import type { ApiReportTemplateDto } from "../../api/index.ts";
import { fileNameFromPath } from "./jobPresentation.ts";
import { invoiceReportBatchLimits } from "./useInvoiceReportBatchOptions.ts";

export function ReportTemplateChecklist({ templates, selected, disabled, onChange }: {
  templates: ApiReportTemplateDto[]; selected: string[]; disabled: boolean; onChange: (paths: string[]) => void;
}) {
  const [search, setSearch] = useState("");
  const visible = templates.filter(template => (template.displayName || template.templatePath).toLocaleLowerCase().includes(search.trim().toLocaleLowerCase()));
  const missing = selected.filter(path => !templates.some(template => template.templatePath === path));
  return <fieldset className="report-template-checklist" disabled={disabled}>
    <legend>报表模板（可多选）</legend>
    {templates.length > 8 && <input aria-label="搜索报表模板" placeholder="搜索模板名称" value={search} onChange={event => setSearch(event.target.value)} />}
    <div className="report-template-checklist-options">
      {visible.map(template => <label className="checkbox-field" key={template.templatePath}>
        <input type="checkbox" value={template.templatePath} checked={selected.includes(template.templatePath)} disabled={disabled || !selected.includes(template.templatePath) && selected.length >= invoiceReportBatchLimits.maxTemplates}
          onChange={event => onChange(event.target.checked ? [...selected, template.templatePath] : selected.filter(path => path !== template.templatePath))} />
        <span>{template.displayName || fileNameFromPath(template.templatePath)}</span>
      </label>)}
      {missing.map(path => <label className="checkbox-field" key={path}><input type="checkbox" checked onChange={() => onChange(selected.filter(value => value !== path))} /><span>已不可用：{path}</span></label>)}
      {!visible.length && <span>{templates.length ? "没有匹配的模板" : "暂无可用模板"}</span>}
    </div>
    <small>已选 {selected.length} 种；每张发票都会生成所选报表。</small>
    {selected.length > 0 && <button className="command-button secondary" type="button" onClick={() => onChange([])}>清空模板选择</button>}
  </fieldset>;
}
