import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import type { ApiCrmCustomerDto, ApiEmailTemplateDto, ApiEmailTemplatePreviewDto, ApiEmailTemplateVariableDto, ApiEmailTemplateVersionDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { TaskViewTabs } from "../../ui/TaskViewTabs.tsx";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import {
  OperationFeedback,
  errorFeedback,
  infoFeedback,
  successFeedback,
  warningFeedback,
  type OperationFeedbackState,
} from "../../ui/OperationFeedback.tsx";
import { TablePrimaryText } from "../../ui/TablePrimaryText.tsx";
import {
  areEmailTemplateDraftsEqual,
  createEmailTemplateCopyName,
  createEmptyEmailTemplateDraft,
  type EmailTemplateDraft,
} from "./emailTemplateModel.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";

type EmailTemplateTaskView = "directory" | "editor" | "variables" | "preview" | "history";
type EmailTemplateScope = "all" | "editable" | "shared";

export function EmailTemplatePage({ client }: { client: ExportDocManagerApiClient }) {
  const templatePermission = useModulePermission("sales.email-templates");
  const crmPermission = useModulePermission("sales.crm");
  const emailPermission = useModulePermission("common.email");
  const navigate = useNavigate();
  const [templates, setTemplates] = useState<ApiEmailTemplateDto[]>([]);
  const [versions, setVersions] = useState<ApiEmailTemplateVersionDto[]>([]);
  const [selectedVersionNumber, setSelectedVersionNumber] = useState(0);
  const [variables, setVariables] = useState<ApiEmailTemplateVariableDto[]>([]);
  const [selectedId, setSelectedId] = useState(0);
  const selectedIdRef = useRef(0);
  const [keyword, setKeyword] = useState("");
  const [includeInactive, setIncludeInactive] = useState(false);
  const [scope, setScope] = useState<EmailTemplateScope>("all");
  const [name, setName] = useState("");
  const [category, setCategory] = useState("通用");
  const [subject, setSubject] = useState("");
  const [bodyHtml, setBodyHtml] = useState("");
  const [isActive, setIsActive] = useState(true);
  const [isShared, setIsShared] = useState(false);
  const [sampleValues, setSampleValues] = useState<Record<string, string>>({});
  const [preview, setPreview] = useState<ApiEmailTemplatePreviewDto | null>(null);
  const [feedback, setFeedback] = useState<OperationFeedbackState | null>(null);
  const [crmCustomerKeyword, setCrmCustomerKeyword] = useState("");
  const [crmCustomers, setCrmCustomers] = useState<ApiCrmCustomerDto[]>([]);
  const [crmCustomerId, setCrmCustomerId] = useState(0);
  const [recipientAddress, setRecipientAddress] = useState("");
  const [view, setView] = useState<EmailTemplateTaskView>("directory");
  const [savedDraft, setSavedDraft] = useState<EmailTemplateDraft>(() => createEmptyEmailTemplateDraft());
  const selected = templates.find((item) => item.id === selectedId);
  const selectedVersion = versions.find((item) => item.versionNumber === selectedVersionNumber) ?? versions[0];
  const visibleTemplates = useMemo(() => templates.filter((item) => {
    if (scope === "editable") return item.canEdit;
    if (scope === "shared") return item.isShared;
    return true;
  }), [scope, templates]);
  const currentDraft = useMemo<EmailTemplateDraft>(() => ({ name, category, subject, bodyHtml, isActive, isShared }), [bodyHtml, category, isActive, isShared, name, subject]);
  const canEdit = templatePermission.canOperate && (!selected || selected.canEdit);
  const canDelete = templatePermission.canManage && selected?.canEdit === true;
  const isDirty = canEdit && !areEmailTemplateDraftsEqual(currentDraft, savedDraft);
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty,
    message: "当前邮件模板有未保存的修改。",
  });

  async function loadTemplates(preferredId?: number, query = { keyword: keyword.trim(), includeInactive }) {
    const rows = await client.listEmailTemplates(query);
    setTemplates(rows);
    const candidateId = preferredId ?? selectedIdRef.current;
    const nextId = candidateId && rows.some((item) => item.id === candidateId) ? candidateId : 0;
    selectTemplateId(nextId);
    if (!nextId) clearEditor();
  }

  async function loadVersions(templateId = selectedIdRef.current, preferredVersion?: number) {
    if (!templateId) { setVersions([]); setSelectedVersionNumber(0); return; }
    const rows = await client.listEmailTemplateVersions({ id: templateId });
    setVersions(rows);
    const nextVersion = preferredVersion && rows.some((item) => item.versionNumber === preferredVersion)
      ? preferredVersion : rows[0]?.versionNumber ?? 0;
    setSelectedVersionNumber(nextVersion);
  }

  function selectTemplateId(id: number) {
    selectedIdRef.current = id;
    setSelectedId(id);
  }

  useEffect(() => {
    const requests: Promise<unknown>[] = [loadTemplates(), client.listEmailTemplateVariables().then((rows) => {
      setVariables(rows);
      setSampleValues(Object.fromEntries(rows.map((item) => [item.key, item.sampleValue])));
    })];
    if (crmPermission.canView) requests.push(searchCrmCustomers(""));
    void Promise.all(requests).catch((error) => setFeedback(errorFeedback(readApiError(error))));
  }, [client, crmPermission.canView, includeInactive]);

  useEffect(() => {
    if (!selected) return;
    setName(selected.name); setCategory(selected.category); setSubject(selected.subject);
    setBodyHtml(selected.bodyHtml); setIsActive(selected.isActive); setIsShared(selected.isShared); setPreview(null);
    setSavedDraft(toDraft(selected));
  }, [selected]);

  function clearEditor() {
    const empty = createEmptyEmailTemplateDraft();
    applyDraft(empty); setSavedDraft(empty); setPreview(null);
  }

  async function save(event: FormEvent) {
    event.preventDefault();
    if (!canEdit) return;
    const id = selected?.id ?? 0;
    const body = { id, name: name.trim(), category: category.trim() || "通用", subject, bodyHtml, isActive, isShared,
      expectedVersion: id > 0 ? selected?.versionNumber ?? 0 : 0 };
    try {
      const saved = id ? await client.updateEmailTemplate({ id, body }) : await client.createEmailTemplate({ body });
      const nextIncludeInactive = includeInactive || !saved.isActive;
      selectTemplateId(saved.id); setKeyword(""); setIncludeInactive(nextIncludeInactive); setSavedDraft(toDraft(saved));
      await loadTemplates(saved.id, { keyword: "", includeInactive: nextIncludeInactive });
      setFeedback(successFeedback(id ? "邮件模板已更新。" : "邮件模板已建立。"));
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); }
  }

  async function remove() {
    if (!canDelete || !selected || !window.confirm(`删除邮件模板“${selected.name}”？`)) return;
    try { await client.deleteEmailTemplate({ id: selected.id }); await loadTemplates(); setView("directory"); setFeedback(successFeedback("邮件模板已删除。")); }
    catch (error) { setFeedback(errorFeedback(readApiError(error))); }
  }

  async function restoreVersion(version: ApiEmailTemplateVersionDto) {
    if (!templatePermission.canOperate || !selected || !version.canRestore || version.versionNumber === selected.versionNumber) return;
    if (!confirmDiscardChanges(`恢复 V${version.versionNumber}`)) return;
    if (!window.confirm(`将模板“${selected.name}”恢复到 V${version.versionNumber}？\n系统会保留现有历史，并生成一个新的当前版本。`)) return;
    try {
      const restored = await client.restoreEmailTemplateVersion({ id: selected.id, versionNumber: version.versionNumber });
      selectTemplateId(restored.id); applyDraft(toDraft(restored)); setSavedDraft(toDraft(restored)); setPreview(null);
      const nextIncludeInactive = includeInactive || !restored.isActive;
      setKeyword(""); setIncludeInactive(nextIncludeInactive);
      await loadTemplates(restored.id, { keyword: "", includeInactive: nextIncludeInactive });
      await loadVersions(restored.id);
      setFeedback(successFeedback(`已从 V${version.versionNumber} 恢复，并生成 V${restored.versionNumber}。`));
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); }
  }

  async function renderPreview() {
    if (!templatePermission.canOperate) return null;
    try {
      const rendered = await client.previewEmailTemplate({ body: { subject, bodyHtml, variables: sampleValues } });
      setPreview(rendered); setFeedback(rendered.unresolvedTokens.length ? warningFeedback(`仍有未识别变量：${rendered.unresolvedTokens.join("、")}`) : null);
      return rendered;
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); return null; }
  }

  async function applyToEmail() {
    if (!emailPermission.canOperate) return;
    const rendered = await renderPreview();
    if (!rendered) return;
    navigate("/tools/email", { state: { emailDraft: { toAddress: recipientAddress, subject: rendered.subject, body: rendered.bodyHtml } } });
  }

  async function previewAndOpen() {
    const rendered = await renderPreview();
    if (rendered) setView("preview");
  }

  function applyDraft(draft: EmailTemplateDraft) {
    setName(draft.name); setCategory(draft.category); setSubject(draft.subject);
    setBodyHtml(draft.bodyHtml); setIsActive(draft.isActive); setIsShared(draft.isShared);
  }

  function startNewTemplate() {
    if (!templatePermission.canOperate) return;
    if (!confirmDiscardChanges("新建模板")) return;
    selectTemplateId(0); clearEditor(); setView("editor"); setFeedback(null);
  }

  function openTemplate(template: ApiEmailTemplateDto) {
    if (template.id !== selectedId && !confirmDiscardChanges(`打开模板“${template.name}”`)) return;
    selectTemplateId(template.id); setView("editor"); setFeedback(null);
  }

  function copyAsNewTemplate() {
    if (!templatePermission.canOperate) return;
    const copiedDraft = {
      ...currentDraft,
      name: createEmailTemplateCopyName(currentDraft.name, templates.map((item) => item.name)),
      isShared: false,
    };
    selectTemplateId(0); applyDraft(copiedDraft); setSavedDraft(createEmptyEmailTemplateDraft());
    setPreview(null); setView("editor"); setFeedback(infoFeedback("已复制为新模板草稿，确认名称后点击保存。原模板保持不变。"));
  }

  function changeView(next: EmailTemplateTaskView) {
    if (next === "preview") { void previewAndOpen(); return; }
    if (next === "history") {
      if (!selected) { setFeedback(warningFeedback("请先从模板目录打开一个已保存模板。")); return; }
      setView(next); void loadVersions(selected.id).catch((error) => setFeedback(errorFeedback(readApiError(error)))); return;
    }
    if (next === "directory" && !confirmDiscardChanges("返回模板目录")) return;
    setView(next);
  }

  async function searchCrmCustomers(searchKeyword = crmCustomerKeyword) {
    if (!crmPermission.canView) return;
    const page = await client.queryCrmCustomers({ keyword: searchKeyword.trim(), status: "", pageNumber: 1, pageSize: 50 });
    setCrmCustomers(page.items);
    setCrmCustomerId((current) => page.items.some((item) => item.id === current) ? current : page.items[0]?.id ?? 0);
  }

  async function loadCrmDraft() {
    if (!crmPermission.canView || !templatePermission.canOperate) return;
    if (!crmCustomerId) { setFeedback(warningFeedback("请选择 CRM 客户。")); return; }
    try {
      const draft = await client.getCrmEmailVariableDraft({ customerId: crmCustomerId });
      const normalizedVariables = Object.fromEntries(Object.entries(draft.variables)
        .map(([key, value]) => [key, typeof value === "string" ? value : ""])) as Record<string, string>;
      setSampleValues((current) => ({ ...current, ...normalizedVariables }));
      setRecipientAddress(draft.toAddress);
      setPreview(null);
      setFeedback(draft.toAddress
        ? successFeedback("已载入客户、主要联系人和建议收件人。")
        : warningFeedback("已载入客户变量；主要联系人尚未填写邮箱。"));
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); }
  }

  return <section className="work-surface">
    <div className="section-heading-row"><div><h2>邮件模板</h2><p>维护单封业务邮件内容；不包含群发、活动、追踪或自动发送。</p></div></div>
    <OperationFeedback feedback={feedback} />
    {!templatePermission.canOperate ? <div className="permission-readonly-notice">当前权限模板仅允许查看邮件模板；新建、复制、预览生成、恢复和修改已禁用。</div> : null}
    <TaskViewTabs value={view} label="邮件模板工作区" onChange={changeView} items={[
      { id: "directory", label: "模板目录" }, { id: "editor", label: selected ? canEdit ? "编辑模板" : "查看模板" : "新建模板", disabled: !selected && !templatePermission.canOperate },
      { id: "variables", label: "变量设置", disabled: !templatePermission.canOperate }, { id: "preview", label: "预览与套用", disabled: !templatePermission.canOperate },
      { id: "history", label: "版本历史" },
    ]} />
    {view === "directory" ? <section className="form-section"><div className="section-header"><div><h3>模板目录</h3><p className="section-description">维护常用的单封业务邮件，不包含群发活动。</p></div><div className="section-header-actions"><span>{visibleTemplates.length} 个</span>{templatePermission.canOperate ? <button className="primary-button" type="button" onClick={startNewTemplate}>新建模板</button> : null}</div></div>
      <form className="toolbar" onSubmit={(event) => { event.preventDefault(); void loadTemplates(); }}>
        <input value={keyword} onChange={(event) => setKeyword(event.target.value)} placeholder="搜索模板名称、主题或正文" />
        <button className="secondary-button">搜索</button>
        <select aria-label="模板范围" value={scope} onChange={(event) => setScope(event.target.value as EmailTemplateScope)}><option value="all">全部模板</option><option value="editable">可维护模板</option><option value="shared">团队共享</option></select>
        <label className="checkbox-field"><input type="checkbox" checked={includeInactive} onChange={(event) => setIncludeInactive(event.target.checked)} />显示停用模板</label>
      </form>
      <div className="table-frame"><table className="data-table responsive-data-table"><thead><tr><th>名称</th><th data-table-priority="secondary">分类</th><th>主题</th><th data-table-priority="secondary">状态与范围</th><th /></tr></thead><tbody>
        {visibleTemplates.map((item) => <tr key={item.id}><td><TablePrimaryText value={item.name} /></td><td data-table-priority="secondary">{item.category}</td><td><TablePrimaryText value={item.subject} /></td><td data-table-priority="secondary"><div className="table-row-actions"><BusinessStatusBadge value={item.isActive ? "启用" : "停用"} />{item.isShared ? <BusinessStatusBadge value="团队共享" /> : null}</div></td><td><button className="secondary-button" type="button" onClick={() => openTemplate(item)}>{templatePermission.canOperate && item.canEdit ? "编辑" : "查看"}</button></td></tr>)}
        {!visibleTemplates.length ? <tr><td className="empty-cell" colSpan={5}><div className="empty-cell-content"><strong>{templates.length ? "当前范围没有模板" : "暂无邮件模板"}</strong><span>{templates.length ? "可切换模板范围，或调整搜索和停用状态条件。" : templatePermission.canOperate ? "先建立一封常用询价、报价或跟进邮件，之后可载入客户变量快速套用。" : "当前没有可查看的邮件模板。"}</span>{!templates.length && templatePermission.canOperate ? <button className="primary-button" type="button" onClick={startNewTemplate}>建立第一个模板</button> : null}</div></td></tr> : null}
      </tbody></table></div>
    </section> : null}
    {view === "editor" ? <form className="form-grid" onSubmit={save}>
        <div className="section-header"><h3>{selected ? "编辑模板" : "新建模板"}</h3><span>{isDirty ? "有未保存修改" : "已同步"}</span></div>
        {!canEdit ? <div className="empty-guidance form-field-wide"><strong>{templatePermission.canOperate ? "这是其他账号共享的模板" : "当前模板为只读"}</strong><span>{templatePermission.canOperate ? "可以预览、套用或复制为自己的模板，但不能直接修改原模板。" : "可以查看正文和版本历史，但当前权限不能生成预览或修改模板。"}</span></div> : null}
        <label>模板名称<input required disabled={!canEdit} maxLength={150} value={name} onChange={(event) => setName(event.target.value)} /></label>
        <label>分类<input disabled={!canEdit} maxLength={50} value={category} onChange={(event) => setCategory(event.target.value)} /></label>
        <label className="form-field-wide">邮件主题<input disabled={!canEdit} maxLength={300} value={subject} onChange={(event) => setSubject(event.target.value)} /></label>
        <label className="form-field-wide">邮件正文<textarea disabled={!canEdit} rows={12} maxLength={10000} value={bodyHtml} onChange={(event) => { setBodyHtml(event.target.value); setPreview(null); }} /><span className="field-hint">普通文字可直接输入；需要格式时可使用简单 HTML。</span></label>
        <label className="checkbox-field"><input type="checkbox" disabled={!canEdit} checked={isActive} onChange={(event) => setIsActive(event.target.checked)} />启用模板</label>
        <label className="checkbox-field"><input type="checkbox" disabled={!canEdit} checked={isShared} onChange={(event) => setIsShared(event.target.checked)} />团队共享（局域网账号可见）</label>
        <div className="form-actions">{canEdit ? <button className="primary-button">保存模板</button> : null}{templatePermission.canOperate ? <button className="secondary-button" type="button" onClick={() => setView("variables")}>设置变量</button> : null}{selected ? <button className="secondary-button" type="button" onClick={() => changeView("history")}>查看版本历史</button> : null}{selected && templatePermission.canOperate ? <button className="secondary-button" type="button" onClick={copyAsNewTemplate}>复制为新模板</button> : null}{canDelete ? <button className="secondary-button danger-button" type="button" onClick={() => void remove()}>删除</button> : null}</div>
      </form> : null}
      {view === "variables" ? <section className="form-section"><div className="section-header"><div><h3>变量设置</h3><p className="section-description">填写预览样例，或把变量插入当前邮件正文。</p></div><span>{variables.length} 项</span></div>
        <div className="context-strip"><strong>{name.trim() || "未命名模板"}</strong><span>{canEdit ? "变量只用于生成当前预览，不会自动发送邮件。" : "这是只读共享模板；可调整预览样例，复制为自己的模板后才能修改正文。"}</span></div>
        <div className="form-grid variable-setting-grid">{variables.map((item) => <label key={item.key}>{item.label}<input value={sampleValues[item.key] ?? ""} onChange={(event) => { setSampleValues((current) => ({ ...current, [item.key]: event.target.value })); setPreview(null); }} /><span className="field-hint">{item.token}</span><button className="secondary-button" disabled={!canEdit} type="button" onClick={() => { setBodyHtml((current) => current + item.token); setPreview(null); }}>插入正文</button></label>)}</div>
        <div className="form-actions"><button className="secondary-button" type="button" onClick={() => setView("editor")}>{canEdit ? "返回编辑正文" : "返回模板详情"}</button>{selected && !canEdit && templatePermission.canOperate ? <button className="secondary-button" type="button" onClick={copyAsNewTemplate}>复制后修改</button> : null}{templatePermission.canOperate ? <button className="primary-button" type="button" onClick={() => void previewAndOpen()}>生成预览</button> : null}</div>
      </section> : null}
      {view === "preview" ? <section className="form-section"><div className="section-header"><div><h3>预览与套用</h3><p className="section-description">可选载入 CRM 客户资料，确认内容后再套用到单封邮件。</p></div></div>
        {!canEdit ? <div className="context-strip"><strong>团队共享模板 · 只读</strong><span>可以载入客户变量并套用邮件；需要修改模板内容时请先复制。</span></div> : null}
        {crmPermission.canView ? <form className="toolbar" onSubmit={(event) => { event.preventDefault(); void searchCrmCustomers(); }}>
          <input value={crmCustomerKeyword} onChange={(event) => setCrmCustomerKeyword(event.target.value)} placeholder="搜索 CRM 客户" />
          <button className="secondary-button">查找客户</button>
          <select value={crmCustomerId} onChange={(event) => setCrmCustomerId(Number(event.target.value))}><option value={0}>请选择客户</option>{crmCustomers.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select>
          <button className="secondary-button" type="button" disabled={!templatePermission.canOperate} onClick={() => void loadCrmDraft()}>载入客户变量</button>
        </form> : <div className="context-strip"><strong>未开放客户资料</strong><span>当前模板没有 CRM 客户读取权限，可继续使用手工样例变量。</span></div>}
        {recipientAddress ? <p>建议收件人：{recipientAddress}</p> : null}
        {preview ? <div className="form-grid"><label className="form-field-wide">预览主题<input readOnly value={preview.subject} /></label><label className="form-field-wide">预览正文<textarea rows={10} readOnly value={preview.bodyHtml} /></label></div> : <p>填写变量样例后点击“预览”。变量值写入 HTML 正文前会进行编码。</p>}
        <div className="form-actions"><button className="secondary-button" type="button" onClick={() => setView("variables")}>调整预览变量</button>{selected && !canEdit && templatePermission.canOperate ? <button className="secondary-button" type="button" onClick={copyAsNewTemplate}>复制后修改</button> : null}<button className="secondary-button" type="button" onClick={() => void renderPreview()}>刷新预览</button>{emailPermission.canOperate ? <button className="primary-button" type="button" onClick={() => void applyToEmail()}>套用到单封邮件</button> : null}</div>
      </section> : null}
      {view === "history" ? <section className="form-section"><div className="section-header"><div><h3>版本历史</h3><p className="section-description">每次实际保存都会追加快照；恢复旧版本时仍保留当前历史。</p></div><span>{selected ? `当前 V${selected.versionNumber}` : "未选择模板"}</span></div>
        <div className="table-frame"><table className="data-table responsive-data-table"><thead><tr><th>版本</th><th>变更</th><th data-table-priority="secondary">操作账号</th><th data-table-priority="secondary">时间</th><th /></tr></thead><tbody>
          {versions.map((item) => <tr key={item.id}><td><strong>V{item.versionNumber}</strong>{item.versionNumber === selected?.versionNumber ? " · 当前" : ""}</td><td>{item.changeType}</td><td data-table-priority="secondary">{item.changedBy || "本地用户"}</td><td data-table-priority="secondary">{new Date(item.createdAt).toLocaleString("zh-CN")}</td><td><button className="secondary-button" type="button" onClick={() => setSelectedVersionNumber(item.versionNumber)}>查看</button></td></tr>)}
          {!versions.length ? <tr><td className="empty-cell" colSpan={5}>暂无可用版本历史。</td></tr> : null}
        </tbody></table></div>
        {selectedVersion ? <div className="form-grid"><div className="context-strip form-field-wide"><strong>V{selectedVersion.versionNumber} · {selectedVersion.changeType}</strong><span>{selectedVersion.category} · {selectedVersion.isActive ? "启用" : "停用"} · {selectedVersion.isShared ? "团队共享" : "个人模板"}</span></div><label className="form-field-wide">历史主题<input readOnly value={selectedVersion.subject} /></label><label className="form-field-wide">历史正文<textarea rows={10} readOnly value={selectedVersion.bodyHtml} /></label><div className="form-actions form-field-wide"><button className="secondary-button" type="button" onClick={() => setView("editor")}>返回模板详情</button>{templatePermission.canOperate && selectedVersion.canRestore && selectedVersion.versionNumber !== selected?.versionNumber ? <button className="primary-button" type="button" onClick={() => void restoreVersion(selectedVersion)}>恢复此版本</button> : null}</div></div> : null}
      </section> : null}
  </section>;
}

function toDraft(template: ApiEmailTemplateDto): EmailTemplateDraft {
  return {
    name: template.name,
    category: template.category,
    subject: template.subject,
    bodyHtml: template.bodyHtml,
    isActive: template.isActive,
    isShared: template.isShared,
  };
}
