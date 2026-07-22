import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useSearchParams } from "react-router-dom";
import type {
  ApiCrmContactDto,
  ApiCrmCustomerDto,
  ApiCrmFollowUpDto,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { CrmPartyManagementPanel } from "./CrmPartyManagementPanel.tsx";
import { CrmCustomerDirectoryPanel } from "./CrmCustomerDirectoryPanel.tsx";
import { CrmCustomerImportPanel } from "./CrmCustomerImportPanel.tsx";
import { TaskViewTabs } from "../../ui/TaskViewTabs.tsx";
import { OperationFeedback, errorFeedback, successFeedback, warningFeedback, type OperationFeedbackState } from "../../ui/OperationFeedback.tsx";
import { BusinessStatusBadge } from "../../ui/BusinessStatusBadge.tsx";
import { TablePrimaryText } from "../../ui/TablePrimaryText.tsx";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { FormGuidance, PageState, PermissionNotice } from "../../ui/PageState.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";

type CustomerFollowUpPageProps = {
  client: ExportDocManagerApiClient;
};

type CustomerTaskView = "followups" | "followup-editor" | "directory" | "profile" | "import";

export function CustomerFollowUpPage({ client }: CustomerFollowUpPageProps) {
  const crmPermission = useModulePermission("sales.crm");
  const requestConfirmation = useConfirmation();
  const [searchParams, setSearchParams] = useSearchParams();
  const [customers, setCustomers] = useState<ApiCrmCustomerDto[]>([]);
  const [contacts, setContacts] = useState<ApiCrmContactDto[]>([]);
  const [rows, setRows] = useState<ApiCrmFollowUpDto[]>([]);
  const [customerId, setCustomerId] = useState(0);
  const [includeCompleted, setIncludeCompleted] = useState(false);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [feedback, setFeedback] = useState<OperationFeedbackState | null>(null);
  const [editingFollowUp, setEditingFollowUp] = useState<ApiCrmFollowUpDto | null>(null);
  const initialView = readCustomerView(searchParams.get("view"));
  const [view, setView] = useState<CustomerTaskView>(initialView);

  function changeView(nextView: CustomerTaskView) {
    setView(nextView);
    setSearchParams(nextView === "followups" ? {} : { view: nextView }, { replace: true });
  }

  useEffect(() => {
    const requestedView = readCustomerView(searchParams.get("view"));
    setView((current) => current === requestedView ? current : requestedView);
  }, [searchParams]);

  const selectedCustomer = useMemo(
    () => customers.find((item) => item.id === customerId),
    [customerId, customers],
  );

  useEffect(() => {
    let stale = false;
    setLoading(true);
    Promise.all([
      client.listCrmCustomers(),
      client.listCrmFollowUps({ includeCompleted, limit: 200 }),
    ])
      .then(([customerRows, followUps]) => {
        if (stale) return;
        setCustomers(customerRows);
        setRows(followUps);
        setCustomerId((current) => current || customerRows[0]?.id || 0);
      })
      .catch((error) => {
        if (!stale) setFeedback(errorFeedback(readApiError(error)));
      })
      .finally(() => {
        if (!stale) setLoading(false);
      });
    return () => {
      stale = true;
    };
  }, [client, includeCompleted]);

  useEffect(() => {
    if (!customerId) {
      setContacts([]);
      return;
    }
    void client.listCrmContacts({ customerId }).then(setContacts).catch((error) => setFeedback(errorFeedback(readApiError(error))));
  }, [client, customerId]);

  async function refresh() {
    setRows(await client.listCrmFollowUps({ includeCompleted, limit: 200 }));
  }

  async function reloadCustomers(preferredId?: number) {
    const nextCustomers = await client.listCrmCustomers();
    setCustomers(nextCustomers);
    const nextId = preferredId && nextCustomers.some((item) => item.id === preferredId)
      ? preferredId
      : nextCustomers[0]?.id ?? 0;
    setCustomerId(nextId);
  }

  async function reloadContacts() {
    setContacts(customerId ? await client.listCrmContacts({ customerId }) : []);
  }

  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!crmPermission.canOperate) return;
    const formElement = event.currentTarget;
    if (!customerId) {
      setFeedback(warningFeedback("请先在基础资料中建立并选择客户。"));
      return;
    }

    const form = new FormData(formElement);
    setSaving(true);
    setFeedback(null);
    try {
      const body = {
          id: 0,
          crmCustomerId: customerId,
          crmContactId: optionalNumber(form.get("crmContactId")),
          type: String(form.get("type") ?? "其他"),
          summary: String(form.get("summary") ?? ""),
          nextAction: String(form.get("nextAction") ?? ""),
          nextFollowUpAt: toIsoDateTime(form.get("nextFollowUpAt")),
          isCompleted: editingFollowUp?.isCompleted ?? false,
          expectedVersion: editingFollowUp?.versionNumber ?? 0,
        };
      if (editingFollowUp) {
        await client.updateCrmFollowUp({ id: editingFollowUp.id, body: { ...body, id: editingFollowUp.id, followedUpAt: editingFollowUp.followedUpAt } });
      } else {
        await client.createCrmFollowUp({ body });
      }
      formElement.reset();
      setEditingFollowUp(null);
      setFeedback(successFeedback(editingFollowUp ? "客户跟进已更新。" : "客户跟进已保存。"));
      await refresh();
      changeView("followups");
    } catch (error) {
      setFeedback(errorFeedback(readApiError(error)));
    } finally {
      setSaving(false);
    }
  }

  async function toggleCompleted(item: ApiCrmFollowUpDto) {
    if (!crmPermission.canOperate) return;
    try {
      await client.updateCrmFollowUp({
        id: item.id,
        body: {
          id: item.id,
          crmCustomerId: item.crmCustomerId,
          crmContactId: item.crmContactId,
          type: item.type,
          summary: item.summary,
          nextAction: item.nextAction,
          followedUpAt: item.followedUpAt,
          nextFollowUpAt: item.nextFollowUpAt,
          isCompleted: !item.isCompleted,
          expectedVersion: item.versionNumber,
        },
      });
      await refresh();
      setFeedback(successFeedback(item.isCompleted ? "跟进记录已恢复为待跟进。" : "跟进记录已标记完成。"));
    } catch (error) {
      setFeedback(errorFeedback(readApiError(error)));
    }
  }

  async function deleteFollowUp(item: ApiCrmFollowUpDto) {
    if (!crmPermission.canManage || !await requestConfirmation({ title: "删除跟进记录", description: `确定删除“${item.customerName}”的这条跟进记录吗？`, confirmLabel: "确认删除", tone: "danger" })) return;
    try {
      await client.deleteCrmFollowUp({ id: item.id });
      await refresh();
      setFeedback(successFeedback("跟进记录已删除。"));
    } catch (error) { setFeedback(errorFeedback(readApiError(error))); }
  }

  return (
    <section className="work-surface">
      <div className="section-heading-row">
        <div>
          <h2>客户跟进</h2>
          <p>销售客户、联系人和跟进独立维护，不修改单证客户、发票或报表资料。</p>
        </div>
        {view === "followups" ? <label className="checkbox-field">
          <input
            type="checkbox"
            checked={includeCompleted}
            onChange={(event) => setIncludeCompleted(event.target.checked)}
          />
          显示已完成
        </label> : null}
      </div>

      <OperationFeedback feedback={feedback} />
      {!crmPermission.canOperate ? <PermissionNotice>当前权限模板仅允许查看客户、联系人和跟进记录；新增、修改、完成状态和导入操作已禁用。</PermissionNotice> : null}

      <TaskViewTabs value={view} label="客户业务工作区" onChange={changeView} items={[
        { id: "followups", label: "跟进记录" }, { id: "followup-editor", label: editingFollowUp ? crmPermission.canOperate ? "编辑跟进" : "查看跟进" : "新增跟进", disabled: !editingFollowUp && !crmPermission.canOperate },
        { id: "directory", label: "客户目录" },
        { id: "profile", label: "客户与联系人" }, { id: "import", label: "客户导入", disabled: !crmPermission.canOperate },
      ]} />

      {view === "profile" ? <CrmPartyManagementPanel
        client={client}
        customers={customers}
        contacts={contacts}
        customerId={customerId}
        onSelectCustomer={setCustomerId}
        onReloadCustomers={reloadCustomers}
        onReloadContacts={reloadContacts}
        onFeedback={setFeedback}
        canOperate={crmPermission.canOperate}
        canManage={crmPermission.canManage}
      /> : null}

      {view === "directory" ? <CrmCustomerDirectoryPanel
        client={client}
        canOperate={crmPermission.canOperate}
        onCreateCustomer={() => changeView("profile")}
        onSelectCustomer={(customer) => { setCustomerId(customer.id); changeView("profile"); }}
      /> : null}
      {view === "import" ? <CrmCustomerImportPanel client={client} canOperate={crmPermission.canOperate} onImported={() => reloadCustomers()} /> : null}

      {view === "followup-editor" ? <form className="form-grid" key={`${editingFollowUp?.id ?? "new"}-${contacts.length}`} onSubmit={handleCreate}>
        <div className="section-heading-row"><h3>{editingFollowUp ? crmPermission.canOperate ? "编辑跟进" : "查看跟进" : "新增跟进"}</h3>
          <button className="secondary-button" type="button" onClick={() => { setEditingFollowUp(null); changeView("followups"); }}>返回跟进记录</button>
        </div>
        {!customers.length ? <FormGuidance className="form-field-wide" title="先建立一位销售客户" description="跟进记录必须归属客户。客户资料与原单证客户相互独立。" action={crmPermission.canOperate ? <button className="primary-button" type="button" onClick={() => changeView("profile")}>建立客户资料</button> : undefined} /> : null}
        <fieldset className="permission-fieldset form-field-wide" disabled={!crmPermission.canOperate}>
        <label>
          客户
          <select value={customerId} onChange={(event) => setCustomerId(Number(event.target.value))}>
            {customers.length === 0 ? <option value={0}>请先建立销售客户</option> : null}
            {customers.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
          </select>
        </label>
        <label>
          联系人
          <select name="crmContactId" defaultValue={editingFollowUp?.crmContactId ?? ""}>
            <option value="">不指定</option>
            {contacts.map((item) => <option key={item.id} value={item.id}>{item.name}{item.title ? ` · ${item.title}` : ""}</option>)}
          </select>
        </label>
        <label>
          跟进方式
          <select name="type" defaultValue={editingFollowUp?.type ?? "邮件"}>
            <option>邮件</option>
            <option>电话</option>
            <option>即时通讯</option>
            <option>拜访</option>
            <option>其他</option>
          </select>
        </label>
        <label className="form-field-wide">
          跟进摘要
          <input name="summary" required maxLength={500} defaultValue={editingFollowUp?.summary} placeholder="例如：客户确认样品，等待价格调整" />
        </label>
        <label className="form-field-wide">
          下次动作
          <input name="nextAction" maxLength={300} defaultValue={editingFollowUp?.nextAction} placeholder="例如：周五发送新版报价" />
        </label>
        <label>
          下次跟进时间
          <input name="nextFollowUpAt" type="datetime-local" defaultValue={toDateTimeLocal(editingFollowUp?.nextFollowUpAt)} />
        </label>
        <div className="form-actions">
          {crmPermission.canOperate ? <button className="primary-button" type="submit" disabled={saving || !selectedCustomer}>
            {saving ? "保存中..." : editingFollowUp ? "更新跟进" : "保存跟进"}
          </button> : null}
        </div>
        </fieldset>
      </form> : null}

      {view === "followups" ? <section className="form-section">
      <div className="section-header">
        <div><h3>跟进记录</h3><p className="section-description">集中查看沟通结果、下一步动作和待办提醒。</p></div>
        {crmPermission.canOperate ? <button className="primary-button" type="button" onClick={() => { setEditingFollowUp(null); changeView("followup-editor"); }}>记录新跟进</button> : null}
      </div>
      <ResponsiveTableFrame label="客户跟进记录" className="table-scroll-region" mobileLayout="scroll" busy={loading}>
        <table className="data-table responsive-data-table follow-up-data-table">
          <thead>
            <tr>
              <th>客户</th>
              <th data-table-priority="secondary">方式</th>
              <th data-table-priority="secondary">联系人</th>
              <th>跟进摘要</th>
              <th data-table-priority="secondary">下次动作</th>
              <th>提醒时间</th>
              <th>状态</th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((item) => (
              <tr key={item.id}>
                <td><TablePrimaryText value={item.customerName} /></td>
                <td data-table-priority="secondary">{item.type}</td>
                <td data-table-priority="secondary"><TablePrimaryText value={item.contactName} /></td>
                <td><TablePrimaryText value={item.summary} /></td>
                <td data-table-priority="secondary"><TablePrimaryText value={item.nextAction} /></td>
                <td>{formatDateTime(item.nextFollowUpAt)}</td>
                <td><BusinessStatusBadge value={item.isCompleted ? "已完成" : isOverdue(item.nextFollowUpAt) ? "已逾期" : "待跟进"} /></td>
                <td>
                  <div className="table-row-actions">
                    <button className="secondary-button" type="button" onClick={() => { setCustomerId(item.crmCustomerId); setEditingFollowUp(item); changeView("followup-editor"); }}>{crmPermission.canOperate ? "编辑" : "查看"}</button>
                    {crmPermission.canOperate ? <button className="secondary-button" type="button" onClick={() => void toggleCompleted(item)}>
                      {item.isCompleted ? "恢复" : "完成"}
                    </button> : null}
                    {crmPermission.canManage ? <button className="secondary-button danger-button" type="button" onClick={() => void deleteFollowUp(item)}>删除</button> : null}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {!loading && rows.length === 0 ? <PageState tone="empty" title={customers.length ? "还没有跟进记录" : "先建立客户，再开始跟进"} description={customers.length ? "记录一次邮件、电话或拜访结果，系统会帮助保留下次动作。" : "销售客户独立维护，不会修改原单证客户、发票或报表资料。"} action={crmPermission.canOperate ? <button className="primary-button" type="button" onClick={() => changeView(customers.length ? "followup-editor" : "profile")}>{customers.length ? "记录第一次跟进" : "建立客户资料"}</button> : undefined} /> : null}
        {loading ? <PageState tone="loading" title="正在加载客户跟进" description="正在读取沟通结果、下一步动作和提醒状态。" /> : null}
      </ResponsiveTableFrame>
      </section> : null}
    </section>
  );
}

function toIsoDateTime(value: FormDataEntryValue | null) {
  const text = String(value ?? "").trim();
  return text ? new Date(text).toISOString() : undefined;
}

function readCustomerView(value: string | null): CustomerTaskView {
  return value === "followup-editor" || value === "directory" || value === "profile" || value === "import" ? value : "followups";
}

function optionalNumber(value: FormDataEntryValue | null) {
  const parsed = Number(String(value ?? ""));
  return Number.isInteger(parsed) && parsed > 0 ? parsed : undefined;
}

function toDateTimeLocal(value?: string) {
  if (!value) return "";
  const date = new Date(value);
  const offset = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - offset).toISOString().slice(0, 16);
}

function formatDateTime(value?: string) {
  if (!value) return "未设置";
  return new Intl.DateTimeFormat("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(value));
}

function isOverdue(value?: string) {
  return Boolean(value && new Date(value).getTime() < Date.now());
}
