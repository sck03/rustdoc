import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import type { ApiCrmCustomerDto, ApiEmailTemplateDto, ApiEmailTemplatePreviewDto, ApiEmailTemplateVariableDto, ApiEmailTemplateVersionDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { TaskViewTabs, getTaskViewPanelProps } from "../../ui/TaskViewTabs.tsx";
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
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { FormGuidance, PermissionNotice } from "../../ui/PageState.tsx";
import {
  areEmailTemplateDraftsEqual,
  createEmailTemplateCopyName,
  createEmptyEmailTemplateDraft,
  type EmailTemplateDraft,
} from "./emailTemplateModel.ts";
import { usePermission } from "../../app/PermissionAccessContext.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { EmailHtmlPreview, EmailRichTextEditor } from "../../ui/EmailRichTextEditor.tsx";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";

type EmailTemplateTaskView = "directory" | "editor" | "variables" | "preview" | "history";
type EmailTemplateScope = "all" | "editable" | "shared";
const emailTemplateTabsId = "email-template-workspace";

export function EmailTemplatePage({ client }: { client: ExportDocManagerApiClient }) {
  const templateViewPermission = usePermission("sales.email-templates", "view");
  const templateEditPermission = usePermission("sales.email-templates", "edit");
  const crmViewPermission = usePermission("sales.customers", "view");
  const emailSendPermission = usePermission("common.email-delivery", "send");
  const requestConfirmation = useConfirmation();
  const navigate = useNavigate();
  const runAbortableOperation = useAbortableOperation();
  const [templates, setTemplates] = useState<ApiEmailTemplateDto[]>([]);
  const [versions, setVersions] = useState<ApiEmailTemplateVersionDto[]>([]);
  const [selectedVersionNumber, setSelectedVersionNumber] = useState(0);
  const [variables, setVariables] = useState<ApiEmailTemplateVariableDto[]>([]);
  const [selectedId, setSelectedId] = useState(0);
  const selectedIdRef = useRef(0);
  const [keyword, setKeyword] = useState("");
  const [includeArchived, setIncludeArchived] = useState(false);
  const [scope, setScope] = useState<EmailTemplateScope>("all");
  const [name, setName] = useState("");
  const [category, setCategory] = useState("通用");
  const [subject, setSubject] = useState("");
  const [bodyHtml, setBodyHtml] = useState("");
  const [shareScope, setShareScope] = useState("Private");
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
    if (scope === "shared") return item.shareScope !== "Private";
    return true;
  }), [scope, templates]);
  const currentDraft = useMemo<EmailTemplateDraft>(() => ({ name, category, subject, bodyHtml }), [bodyHtml, category, name, subject]);
  const canEdit = templateEditPermission.allowed && (!selected || selected.canEdit);
  const canDelete = selected?.canArchive === true;
  const isDirty = canEdit && !areEmailTemplateDraftsEqual(currentDraft, savedDraft);
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty,
    message: "当前邮件模板有未保存的修改。",
  });

  async function loadTemplates(preferredId?: number, query = { keyword: keyword.trim(), includeArchived }) {
    await runAbortableOperation(async (signal) => {
      const rows = await client.listEmailTemplates(query, { signal });
      if (signal.aborted) return;
      setTemplates(rows);
      const candidateId = preferredId ?? selectedIdRef.current;
      const nextId = candidateId && rows.some((item) => item.id === candidateId) ? candidateId : 0;
      selectTemplateId(nextId);
      if (!nextId) clearEditor();
    });
  }

  async function loadVersions(templateId = selectedIdRef.current, preferredVersion?: number) {
    if (!templateId) { setVersions([]); setSelectedVersionNumber(0); return; }
    await runAbortableOperation(async (signal) => {
      const rows = await client.listEmailTemplateVersions({ id: templateId }, { signal });
      if (signal.aborted) return;
      setVersions(rows);
      const nextVersion = preferredVersion && rows.some((item) => item.versionNumber === preferredVersion)
        ? preferredVersion : rows[0]?.versionNumber ?? 0;
      setSelectedVersionNumber(nextVersion);
    });
  }

  function selectTemplateId(id: number) {
    selectedIdRef.current = id;
    setSelectedId(id);
  }

  useEffect(() => {
    const requests: Promise<unknown>[] = [loadTemplates(), runAbortableOperation(async (signal) => {
      const rows = await client.listEmailTemplateVariables({ signal });
      if (signal.aborted) return;
      setVariables(rows);
      setSampleValues(Object.fromEntries(rows.map((item) => [item.key, item.sampleValue])));
    })];
    if (crmViewPermission.allowed) requests.push(searchCrmCustomers(""));
    void Promise.all(requests).catch((error) => {
      if (!isAbortError(error)) setFeedback(errorFeedback(readApiError(error)));
    });
  }, [client, crmViewPermission.allowed, includeArchived]);

  useEffect(() => {
    if (!selected) return;
    setName(selected.name); setCategory(selected.category); setSubject(selected.subject);
    setBodyHtml(selected.bodyHtml); setShareScope(selected.shareScope); setPreview(null);
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
    const body = { name: name.trim(), category: category.trim() || "通用", subject, bodyHtml,
      expectedVersion: id > 0 ? selected?.versionNumber ?? 0 : 0 };
    try {
      const saved = await runAbortableOperation((signal) => id
        ? client.saveEmailTemplateDraft({ id, body }, { signal })
        : client.createEmailTemplate({ body }, { signal }));
      selectTemplateId(saved.id); setKeyword(""); setSavedDraft(toDraft(saved));
      await loadTemplates(saved.id, { keyword: "", includeArchived });
      setFeedback(successFeedback(id ? "邮件模板草稿已保存。" : "邮件模板草稿已建立。"));
    } catch (error) {
      if (!isAbortError(error)) setFeedback(errorFeedback(readApiError(error)));
    }
  }

  async function remove() {
    if (!canDelete || !selected || !await requestConfirmation({ title: "归档邮件模板", description: `确定归档邮件模板“${selected.name}”吗？`, details: ["归档保留版本历史，可由具备恢复权限的人员重新恢复为私有草稿。"], confirmLabel: "确认归档", tone: "danger" })) return;
    try {
      await runAbortableOperation((signal) => client.archiveEmailTemplate({ id: selected.id, expectedVersion: selected.versionNumber }, { signal }));
      await loadTemplates();
      setView("directory");
      setFeedback(successFeedback("邮件模板已归档。"));
    } catch (error) {
      if (!isAbortError(error)) setFeedback(errorFeedback(readApiError(error)));
    }
  }

  async function updateLifecycle(action: "publish" | "share" | "disable" | "restore") {
    if (!selected) return;
    if (isDirty && !await confirmDiscardChanges("执行模板生命周期操作")) return;
    const labels = { publish: "发布", share: "调整共享范围", disable: "停用", restore: "恢复" } as const;
    if (!await requestConfirmation({
      title: `${labels[action]}邮件模板`,
      description: `确定对“${selected.name}”执行${labels[action]}吗？`,
      details: action === "share" ? ["共享范围只影响已发布模板，不会保存编辑器中的未保存正文。"] : undefined,
      confirmLabel: `确认${labels[action]}`,
    })) return;

    try {
      const saved = await runAbortableOperation((signal) => {
        const body = { expectedVersion: selected.versionNumber };
        switch (action) {
          case "publish": return client.publishEmailTemplate({ id: selected.id, body }, { signal });
          case "share": return client.shareEmailTemplate({ id: selected.id, body: { ...body, shareScope } }, { signal });
          case "disable": return client.disableEmailTemplate({ id: selected.id, body }, { signal });
          case "restore": return client.restoreEmailTemplate({ id: selected.id, body }, { signal });
        }
      });
      selectTemplateId(saved.id);
      applyDraft(toDraft(saved));
      setSavedDraft(toDraft(saved));
      setShareScope(saved.shareScope);
      await loadTemplates(saved.id, { keyword: "", includeArchived });
      await loadVersions(saved.id);
      setFeedback(successFeedback(`邮件模板已${labels[action]}。`));
    } catch (error) {
      if (!isAbortError(error)) setFeedback(errorFeedback(readApiError(error)));
    }
  }

  async function restoreVersion(version: ApiEmailTemplateVersionDto) {
    if (!selected || !version.canRestore || version.versionNumber === selected.versionNumber) return;
    if (!await confirmDiscardChanges(`恢复 V${version.versionNumber}`)) return;
    if (!await requestConfirmation({ title: `恢复到 V${version.versionNumber}`, description: `将模板“${selected.name}”恢复到历史版本。`, details: ["系统会保留现有历史，并生成一个新的当前版本。"], confirmLabel: "确认恢复" })) return;
    try {
      const restored = await runAbortableOperation((signal) => client.restoreEmailTemplateVersion(
        { id: selected.id, versionNumber: version.versionNumber, body: { expectedVersion: selected.versionNumber } },
        { signal },
      ));
      selectTemplateId(restored.id); applyDraft(toDraft(restored)); setSavedDraft(toDraft(restored)); setPreview(null);
      setKeyword("");
      await loadTemplates(restored.id, { keyword: "", includeArchived });
      await loadVersions(restored.id);
      setFeedback(successFeedback(`已从 V${version.versionNumber} 恢复，并生成 V${restored.versionNumber}。`));
    } catch (error) {
      if (!isAbortError(error)) setFeedback(errorFeedback(readApiError(error)));
    }
  }

  async function renderPreview() {
    if (!templateViewPermission.allowed) return null;
    try {
      const rendered = await runAbortableOperation((signal) => client.previewEmailTemplate(
        { body: { subject, bodyHtml, variables: sampleValues } },
        { signal },
      ));
      setPreview(rendered); setFeedback(rendered.unresolvedTokens.length ? warningFeedback(`仍有未识别变量：${rendered.unresolvedTokens.join("、")}`) : null);
      return rendered;
    } catch (error) {
      if (!isAbortError(error)) setFeedback(errorFeedback(readApiError(error)));
      return null;
    }
  }

  async function applyToEmail() {
    if (!emailSendPermission.allowed) return;
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
    setBodyHtml(draft.bodyHtml);
  }

  async function startNewTemplate() {
    if (!templateEditPermission.allowed) return;
    if (!await confirmDiscardChanges("新建模板")) return;
    selectTemplateId(0); clearEditor(); setView("editor"); setFeedback(null);
  }

  async function openTemplate(template: ApiEmailTemplateDto) {
    if (template.id !== selectedId && !await confirmDiscardChanges(`打开模板“${template.name}”`)) return;
    selectTemplateId(template.id); setView("editor"); setFeedback(null);
  }

  function copyAsNewTemplate() {
    if (!templateEditPermission.allowed) return;
    const copiedDraft = {
      ...currentDraft,
      name: createEmailTemplateCopyName(currentDraft.name, templates.map((item) => item.name)),
    };
    selectTemplateId(0); applyDraft(copiedDraft); setSavedDraft(createEmptyEmailTemplateDraft());
    setPreview(null); setView("editor"); setFeedback(infoFeedback("已复制为新模板草稿，确认名称后点击保存。原模板保持不变。"));
  }

  async function changeView(next: EmailTemplateTaskView) {
    if (next === "preview") { void previewAndOpen(); return; }
    if (next === "history") {
      if (!selected) { setFeedback(warningFeedback("请先从模板目录打开一个已保存模板。")); return; }
      setView(next);
      void loadVersions(selected.id).catch((error) => {
        if (!isAbortError(error)) setFeedback(errorFeedback(readApiError(error)));
      });
      return;
    }
    if (next === "directory" && !await confirmDiscardChanges("返回模板目录")) return;
    setView(next);
  }

  async function searchCrmCustomers(searchKeyword = crmCustomerKeyword) {
    if (!crmViewPermission.allowed) return;
    try {
      const page = await runAbortableOperation((signal) => client.queryCrmCustomers(
        { keyword: searchKeyword.trim(), status: "", pageNumber: 1, pageSize: 50 },
        { signal },
      ));
      setCrmCustomers(page.items);
      setCrmCustomerId((current) => page.items.some((item) => item.id === current) ? current : page.items[0]?.id ?? 0);
    } catch (error) {
      if (!isAbortError(error)) setFeedback(errorFeedback(readApiError(error)));
    }
  }

  async function loadCrmDraft() {
    if (!crmViewPermission.allowed || !templateViewPermission.allowed) return;
    if (!crmCustomerId) { setFeedback(warningFeedback("请选择 CRM 客户。")); return; }
    try {
      const draft = await runAbortableOperation((signal) => client.getCrmEmailVariableDraft({ customerId: crmCustomerId }, { signal }));
      const normalizedVariables = Object.fromEntries(Object.entries(draft.variables)
        .map(([key, value]) => [key, typeof value === "string" ? value : ""])) as Record<string, string>;
      setSampleValues((current) => ({ ...current, ...normalizedVariables }));
      setRecipientAddress(draft.toAddress);
      setPreview(null);
      setFeedback(draft.toAddress
        ? successFeedback("已载入客户、主要联系人和建议收件人。")
        : warningFeedback("已载入客户变量；主要联系人尚未填写邮箱。"));
    } catch (error) {
      if (!isAbortError(error)) setFeedback(errorFeedback(readApiError(error)));
    }
  }

  return <section className="work-surface">
    <div className="section-heading-row"><div><h2>邮件模板</h2><p>维护单封业务邮件内容；不包含群发、活动、追踪或自动发送。</p></div></div>
    <OperationFeedback feedback={feedback} />
    {!templateEditPermission.allowed ? <PermissionNotice>当前权限只允许查看和使用邮件模板；正文编辑、发布、共享和归档分别由服务端能力控制。</PermissionNotice> : null}
    <TaskViewTabs idPrefix={emailTemplateTabsId} value={view} label="邮件模板工作区" onChange={changeView} items={[
      { id: "directory", label: "模板目录" }, { id: "editor", label: selected ? canEdit ? "编辑模板" : "查看模板" : "新建模板", disabled: !selected && !templateEditPermission.allowed },
      { id: "variables", label: "变量设置", disabled: !templateViewPermission.allowed }, { id: "preview", label: "预览与套用", disabled: !templateViewPermission.allowed },
      { id: "history", label: "版本历史" },
    ]} />
    {view === "directory" ? <section className="form-section" {...getTaskViewPanelProps(emailTemplateTabsId, "directory")}><div className="section-header"><div><h3>模板目录</h3><p className="section-description">维护常用的单封业务邮件，不包含群发活动。</p></div><div className="section-header-actions"><span>{visibleTemplates.length} 个</span>{templateEditPermission.allowed ? <button className="primary-button" type="button" onClick={startNewTemplate}>新建模板</button> : null}</div></div>
      <form className="toolbar" onSubmit={(event) => {
        event.preventDefault();
        void loadTemplates().catch((error) => {
          if (!isAbortError(error)) setFeedback(errorFeedback(readApiError(error)));
        });
      }}>
        <input value={keyword} onChange={(event) => setKeyword(event.target.value)} placeholder="搜索模板名称、主题或正文" />
        <button className="secondary-button" type="submit">搜索</button>
        <select aria-label="模板范围" value={scope} onChange={(event) => setScope(event.target.value as EmailTemplateScope)}><option value="all">全部模板</option><option value="editable">可维护模板</option><option value="shared">团队共享</option></select>
        <label className="checkbox-field"><input type="checkbox" checked={includeArchived} onChange={(event) => setIncludeArchived(event.target.checked)} />显示归档模板</label>
      </form>
      <ResponsiveTableFrame label="邮件模板列表" mobileLayout="scroll"><table className="data-table responsive-data-table"><thead><tr><th>名称</th><th data-table-priority="secondary">分类</th><th>主题</th><th data-table-priority="secondary">状态与范围</th><th /></tr></thead><tbody>
        {visibleTemplates.map((item) => <tr key={item.id}><td><TablePrimaryText value={item.name} /></td><td data-table-priority="secondary">{item.category}</td><td><TablePrimaryText value={item.subject} /></td><td data-table-priority="secondary"><div className="table-row-actions"><BusinessStatusBadge value={emailTemplateStatusLabel(item.status)} />{item.shareScope !== "Private" ? <BusinessStatusBadge value={emailTemplateShareScopeLabel(item.shareScope)} /> : null}</div></td><td><button className="secondary-button" type="button" onClick={() => openTemplate(item)}>{item.canEdit ? "编辑" : "查看"}</button></td></tr>)}
        {!visibleTemplates.length ? <tr><td className="empty-cell" colSpan={5}><div className="empty-cell-content"><strong>{templates.length ? "当前范围没有模板" : "暂无邮件模板"}</strong><span>{templates.length ? "可切换模板范围，或调整搜索和归档状态条件。" : templateEditPermission.allowed ? "先建立一封常用询价、报价或跟进邮件，之后可载入客户变量快速套用。" : "当前没有可查看的邮件模板。"}</span>{!templates.length && templateEditPermission.allowed ? <button className="primary-button" type="button" onClick={startNewTemplate}>建立第一个模板</button> : null}</div></td></tr> : null}
      </tbody></table></ResponsiveTableFrame>
    </section> : null}
    {view === "editor" ? <form className="form-grid" onSubmit={save} {...getTaskViewPanelProps(emailTemplateTabsId, "editor")}>
        <div className="section-header"><h3>{selected ? "编辑模板" : "新建模板"}</h3><span>{isDirty ? "有未保存修改" : "已同步"}</span></div>
        {!canEdit ? <FormGuidance className="form-field-wide" title={templateViewPermission.allowed ? "当前模板正文只读" : "当前模板不可访问"} description={templateViewPermission.allowed ? "可以预览或套用；只有模板所有者并具有编辑动作权限时才能修改正文。" : "当前权限不能查看或修改模板。"} /> : null}
        <label>模板名称<input required disabled={!canEdit} maxLength={150} value={name} onChange={(event) => setName(event.target.value)} /></label>
        <label>分类<input disabled={!canEdit} maxLength={50} value={category} onChange={(event) => setCategory(event.target.value)} /></label>
        <label className="form-field-wide">邮件主题<input disabled={!canEdit} maxLength={300} value={subject} onChange={(event) => setSubject(event.target.value)} /></label>
        <div className="form-field-wide email-rich-text-field">
          <span className="email-rich-text-field-label">邮件正文</span>
          <EmailRichTextEditor value={bodyHtml} disabled={!canEdit} onChange={(value) => { setBodyHtml(value); setPreview(null); }} />
          <span className="field-hint">可直接排版文字、列表和链接；图片请作为邮件附件发送。</span>
          <details className="email-html-advanced">
            <summary>高级 HTML</summary>
            <textarea aria-label="邮件正文高级 HTML" disabled={!canEdit} rows={8} maxLength={10000} value={bodyHtml} onChange={(event) => { setBodyHtml(event.target.value); setPreview(null); }} />
            <span className="field-hint">仅建议熟悉 HTML 的用户使用。系统会移除脚本、事件属性、嵌入内容和危险链接。</span>
          </details>
        </div>
        {selected ? <div className="context-strip form-field-wide"><strong>{emailTemplateStatusLabel(selected.status)}</strong><span>{emailTemplateShareScopeLabel(selected.shareScope)} · V{selected.versionNumber}</span></div> : null}
        {selected?.canShare ? <label>共享范围<select value={shareScope} onChange={(event) => setShareScope(event.target.value)}><option value="Private">仅自己可见</option><option value="Department">同部门可见</option><option value="Company">同公司可见</option><option value="All">全部成员可见</option></select></label> : null}
        <div className="form-actions">
          {canEdit ? <button className="primary-button" type="submit">保存草稿</button> : null}
          {selected?.canPublish ? <button className="secondary-button" type="button" onClick={() => void updateLifecycle("publish")}>发布</button> : null}
          {selected?.canShare ? <button className="secondary-button" type="button" onClick={() => void updateLifecycle("share")}>应用共享范围</button> : null}
          {selected?.canDisable ? <button className="secondary-button" type="button" onClick={() => void updateLifecycle("disable")}>停用</button> : null}
          {selected?.canRestore ? <button className="secondary-button" type="button" onClick={() => void updateLifecycle("restore")}>恢复</button> : null}
          {templateViewPermission.allowed ? <button className="secondary-button" type="button" onClick={() => setView("variables")}>设置变量</button> : null}
          {selected ? <button className="secondary-button" type="button" onClick={() => changeView("history")}>查看版本历史</button> : null}
          {selected && templateEditPermission.allowed ? <button className="secondary-button" type="button" onClick={copyAsNewTemplate}>复制为新模板</button> : null}
          {canDelete ? <button className="secondary-button danger-button" type="button" onClick={() => void remove()}>归档</button> : null}
        </div>
      </form> : null}
      {view === "variables" ? <section className="form-section" {...getTaskViewPanelProps(emailTemplateTabsId, "variables")}><div className="section-header"><div><h3>变量设置</h3><p className="section-description">填写预览样例，或把变量插入当前邮件正文。</p></div><span>{variables.length} 项</span></div>
        <div className="context-strip"><strong>{name.trim() || "未命名模板"}</strong><span>{canEdit ? "变量只用于生成当前预览，不会自动发送邮件。" : "这是只读共享模板；可调整预览样例，复制为自己的模板后才能修改正文。"}</span></div>
        <div className="form-grid variable-setting-grid">{variables.map((item) => <label key={item.key}>{item.label}<input value={sampleValues[item.key] ?? ""} onChange={(event) => { setSampleValues((current) => ({ ...current, [item.key]: event.target.value })); setPreview(null); }} /><span className="field-hint">{item.token}</span><button className="secondary-button" disabled={!canEdit} type="button" onClick={() => { setBodyHtml((current) => `${current}<p>${item.token}</p>`); setPreview(null); }}>插入正文</button></label>)}</div>
        <div className="form-actions"><button className="secondary-button" type="button" onClick={() => setView("editor")}>{canEdit ? "返回编辑正文" : "返回模板详情"}</button>{selected && !canEdit && templateEditPermission.allowed ? <button className="secondary-button" type="button" onClick={copyAsNewTemplate}>复制后修改</button> : null}{templateViewPermission.allowed ? <button className="primary-button" type="button" onClick={() => void previewAndOpen()}>生成预览</button> : null}</div>
      </section> : null}
      {view === "preview" ? <section className="form-section" {...getTaskViewPanelProps(emailTemplateTabsId, "preview")}><div className="section-header"><div><h3>预览与套用</h3><p className="section-description">可选载入 CRM 客户资料，确认内容后再套用到单封邮件。</p></div></div>
        {!canEdit ? <div className="context-strip"><strong>团队共享模板 · 只读</strong><span>可以载入客户变量并套用邮件；需要修改模板内容时请先复制。</span></div> : null}
        {crmViewPermission.allowed ? <form className="toolbar" onSubmit={(event) => {
          event.preventDefault();
          void searchCrmCustomers().catch((error) => {
            if (!isAbortError(error)) setFeedback(errorFeedback(readApiError(error)));
          });
        }}>
          <input value={crmCustomerKeyword} onChange={(event) => setCrmCustomerKeyword(event.target.value)} placeholder="搜索 CRM 客户" />
          <button className="secondary-button" type="submit">查找客户</button>
          <select value={crmCustomerId} onChange={(event) => setCrmCustomerId(Number(event.target.value))}><option value={0}>请选择客户</option>{crmCustomers.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select>
          <button className="secondary-button" type="button" disabled={!templateViewPermission.allowed} onClick={() => void loadCrmDraft()}>载入客户变量</button>
        </form> : <div className="context-strip"><strong>未开放客户资料</strong><span>当前模板没有 CRM 客户读取权限，可继续使用手工样例变量。</span></div>}
        {recipientAddress ? <p>建议收件人：{recipientAddress}</p> : null}
        {preview ? <div className="form-grid"><label className="form-field-wide">预览主题<input readOnly value={preview.subject} /></label><div className="form-field-wide email-rich-text-field"><span className="email-rich-text-field-label">预览正文</span><EmailHtmlPreview html={preview.bodyHtml} title="邮件模板正文预览" /></div></div> : <p>填写变量样例后点击“预览”。变量值写入邮件正文前会安全编码。</p>}
        <div className="form-actions"><button className="secondary-button" type="button" onClick={() => setView("variables")}>调整预览变量</button>{selected && !canEdit && templateEditPermission.allowed ? <button className="secondary-button" type="button" onClick={copyAsNewTemplate}>复制后修改</button> : null}<button className="secondary-button" type="button" onClick={() => void renderPreview()}>刷新预览</button>{emailSendPermission.allowed ? <button className="primary-button" type="button" onClick={() => void applyToEmail()}>套用到单封邮件</button> : null}</div>
      </section> : null}
      {view === "history" ? <section className="form-section" {...getTaskViewPanelProps(emailTemplateTabsId, "history")}><div className="section-header"><div><h3>版本历史</h3><p className="section-description">每次实际保存都会追加快照；恢复旧版本时仍保留当前历史。</p></div><span>{selected ? `当前 V${selected.versionNumber}` : "未选择模板"}</span></div>
        <ResponsiveTableFrame label="邮件模板版本历史" mobileLayout="scroll"><table className="data-table responsive-data-table"><thead><tr><th>版本</th><th>变更</th><th data-table-priority="secondary">操作账号</th><th data-table-priority="secondary">时间</th><th /></tr></thead><tbody>
          {versions.map((item) => <tr key={item.id}><td><strong>V{item.versionNumber}</strong>{item.versionNumber === selected?.versionNumber ? " · 当前" : ""}</td><td>{item.changeType}</td><td data-table-priority="secondary">{item.changedBy || "本地用户"}</td><td data-table-priority="secondary">{new Date(item.createdAt).toLocaleString("zh-CN")}</td><td><button className="secondary-button" type="button" onClick={() => setSelectedVersionNumber(item.versionNumber)}>查看</button></td></tr>)}
          {!versions.length ? <tr><td className="empty-cell" colSpan={5}>暂无可用版本历史。</td></tr> : null}
        </tbody></table></ResponsiveTableFrame>
        {selectedVersion ? <div className="form-grid"><div className="context-strip form-field-wide"><strong>V{selectedVersion.versionNumber} · {selectedVersion.changeType}</strong><span>{selectedVersion.category} · {emailTemplateStatusLabel(selectedVersion.status)} · {emailTemplateShareScopeLabel(selectedVersion.shareScope)}</span></div><label className="form-field-wide">历史主题<input readOnly value={selectedVersion.subject} /></label><div className="form-field-wide email-rich-text-field"><span className="email-rich-text-field-label">历史正文</span><EmailHtmlPreview html={selectedVersion.bodyHtml} title={`邮件模板 V${selectedVersion.versionNumber} 正文`} /><details className="email-html-advanced"><summary>查看高级 HTML</summary><textarea aria-label="历史邮件正文高级 HTML" rows={8} readOnly value={selectedVersion.bodyHtml} /></details></div><div className="form-actions form-field-wide"><button className="secondary-button" type="button" onClick={() => setView("editor")}>返回模板详情</button>{selectedVersion.canRestore && selectedVersion.versionNumber !== selected?.versionNumber ? <button className="primary-button" type="button" onClick={() => void restoreVersion(selectedVersion)}>恢复此版本</button> : null}</div></div> : null}
      </section> : null}
  </section>;
}

function toDraft(template: ApiEmailTemplateDto): EmailTemplateDraft {
  return {
    name: template.name,
    category: template.category,
    subject: template.subject,
    bodyHtml: template.bodyHtml,
  };
}

function emailTemplateStatusLabel(status?: string) {
  switch (status) {
    case "Draft": return "草稿";
    case "Published": return "已发布";
    case "Disabled": return "已停用";
    case "Archived": return "已归档";
    default: return "未知状态";
  }
}

function emailTemplateShareScopeLabel(scope?: string) {
  switch (scope) {
    case "Department": return "同部门可见";
    case "Company": return "同公司可见";
    case "All": return "全部成员可见";
    default: return "仅自己可见";
  }
}
