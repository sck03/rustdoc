import { EditorWorkspace } from "../../ui/layout/Workspaces.tsx";
import { Button } from "../../ui/Button.tsx";
import { Link, useSearchParams } from "react-router-dom";
import { useState } from "react";
import type {
  ApiUserDto,
  ExportDocManagerApiClient,
  OaApprovalSettings,
  OaApprovalRule,
  OaRoutingUnit,
} from "../../api/index.ts";
import { ApprovalSimulationDialog } from "./ApprovalSimulationDialog.tsx";
import { InlineNotice, PageState } from "../../ui/PageState.tsx";
import { SegmentedNav } from "../../ui/primitives/Tabs.tsx";
import { ApprovalDefinitionList } from "./ApprovalDefinitionList.tsx";
import { ApprovalActivationsPanel } from "./ApprovalActivationsPanel.tsx";
import { createApplication, ruleLabel } from "./approvalSettingsModel.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { ApprovalDelegationsEditor } from "./ApprovalDelegationsEditor.tsx";
import { OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import {
  useApprovalSettings,
  useApprovalSettingsDraft,
  type ApprovalAccount,
  type ApprovalSettingsField,
} from "./useApprovalSettings.ts";
import { HandlingServicesEditor } from "./HandlingServicesEditor.tsx";
import { ApprovalGraphEditor } from "./ApprovalGraphEditor.tsx";
import { ApprovalSheetTemplates } from "./ApprovalSheetTemplates.tsx";
import { UnitWorkflowTasksPanel } from "./UnitWorkflowTasksPanel.tsx";
import { ApprovalDefinitionHistory } from "./ApprovalDefinitionHistory.tsx";
import { WorkflowExceptionsPanel } from "./WorkflowExceptionsPanel.tsx";
import { BusinessCalendarEditor } from "./BusinessCalendarEditor.tsx";
import { OaFormDesigner } from "./OaFormDesigner.tsx";
import "../../styles/routes/office.css";
import "../../styles/routes/oa.css";

const views = [
  { key: "definitions", label: "流程定义" },
  { key: "calendar", label: "工作日历" },
  { key: "handling", label: "办理分工" },
  { key: "delegations", label: "审批代理" },
  { key: "templates", label: "审批单模板" },
  { key: "runtime", label: "流程运行" },
] as const;
const editorTabs = [
  { key: "basic", label: "基本信息" },
  { key: "graph", label: "节点图" },
  { key: "notify", label: "抄送与通知" },
  { key: "form", label: "申请表单" },
  { key: "history", label: "版本与历史" },
  { key: "publish", label: "仿真与发布" },
] as const;

export function ApprovalSettingsPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const query = useApprovalSettings(client, user);
  if (!user.capabilities.canManageUsers && !user.capabilities.unitManagement?.workflows)
    return <PageState tone="permission" title="需要当前单位的流程管理授权" />;
  if (query.isPending) return <PageState tone="loading" title="正在读取审批设置" />;
  if (!query.data)
    return (
      <PageState
        tone="error"
        title="审批设置读取失败"
        description={readApiError(query.error)}
        action={
          <Button type="button" onClick={() => void query.refetch()}>
            重试
          </Button>
        }
      />
    );
  return (
    <ApprovalSettingsEditor
      key={`${user.id}:${user.companyScope}:${user.contextId}`}
      client={client}
      user={user}
      initial={query.data.settings}
      accounts={query.data.accounts}
      handlers={query.data.handlers}
      units={query.data.routingUnits}
    />
  );
}
function ApprovalSettingsEditor({
  client,
  user,
  initial,
  accounts,
  handlers,
  units,
}: {
  client: ExportDocManagerApiClient;
  user: ApiUserDto;
  initial: OaApprovalSettings;
  accounts: ApprovalAccount[];
  handlers: ApprovalAccount[];
  units: OaRoutingUnit[];
}) {
  const [params, setParams] = useSearchParams();
  const view = views.find((item) => item.key === params.get("view"))?.key ?? "definitions";
  const tab = editorTabs.find((item) => item.key === params.get("tab"))?.key ?? "basic";
  const [simulation, setSimulation] = useState<{
    rule: OaApprovalRule;
    publish: boolean;
  } | null>(null);
  const {
    draft,
    setDraft,
    operation,
    formRef,
    changeRule,
    save,
    publish,
    disable,
    remove,
    cancelActivation,
    reload,
    calendarInputs,
    changeCalendarInput,
    changedElsewhere,
  } = useApprovalSettingsDraft(client, initial);
  const index = draft.rules.findIndex((rule) => rule.key === params.get("definition"));
  const rule = index >= 0 ? draft.rules[index] : null;
  const publication = rule && draft.publications.find((item) => item.key === rule.key);
  const section: ApprovalSettingsField =
    view === "calendar"
      ? "calendar"
      : view === "handling"
        ? "handlingServices"
        : view === "delegations"
          ? "delegations"
          : view === "templates"
            ? "sheetTemplates"
            : "rules";
  const update = (patch: Record<string, string | null>) =>
    setParams((current) => {
      const next = new URLSearchParams(current);
      for (const [key, value] of Object.entries(patch)) {
        if (value === null) next.delete(key);
        else next.set(key, value);
      }
      return next;
    });
  const simulate = (publish: boolean) => {
    if (rule) setSimulation({ rule, publish });
  };
  const addApplication = (source?: OaApprovalRule) => {
    const template = source ?? draft.rules.find((rule) => rule.key === "general");
    if (!template) return;
    const created = createApplication(template, Boolean(source));
    setDraft((value) => ({ ...value, rules: [...value.rules, created] }));
    update({ definition: created.key, tab: "basic" });
  };
  return (
    <EditorWorkspace
      className="office-workspace oa-workspace"
      title="流程管理"
      actions={<Link to="/office/approvals">返回申请与审批</Link>}
      description="在途申请保留提交时的流程版本。保存当前分区或当前流程，其他未保存草稿继续保留；发布和停用分别办理。"
    >
      <SegmentedNav
        label="流程管理分区"
        value={view}
        items={views}
        onChange={(view) => update({ view, definition: null, tab: null })}
      />
      {changedElsewhere && (
        <InlineNotice tone="warning">设置已被其他管理员修改。当前草稿已保留，请核对后重新载入最新设置。</InlineNotice>
      )}
      {view !== "runtime" && (
        <form
          ref={formRef}
          onInvalidCapture={(event) => {
            const section = (event.target as HTMLElement).closest("details");
            if (section) section.open = true;
          }}
          onSubmit={(event) => {
            event.preventDefault();
            void save(section, rule?.key);
          }}
        >
          <fieldset disabled={operation.busy} className="office-form-grid">
            <legend>{views.find((item) => item.key === view)?.label}</legend>
            {view === "definitions" && !rule && (
              <Button type="button" onClick={() => addApplication()}>
                新建审批事项
              </Button>
            )}
            {view === "definitions" && !rule && (
              <ApprovalDefinitionList
                rules={draft.rules}
                publications={draft.publications}
                onSelect={(key) => update({ definition: key, tab: "basic" })}
              />
            )}
            {view === "definitions" && rule && (
              <section className="office-field-wide" data-definition-key={rule.key} aria-label={ruleLabel(rule)}>
                <div className="section-header">
                  <h2>{ruleLabel(rule)}</h2>
                  <Button type="button" onClick={() => update({ definition: null, tab: null })}>
                    返回定义列表
                  </Button>
                </div>
                <SegmentedNav
                  label="流程定义编辑"
                  value={tab}
                  items={editorTabs.filter((item) => item.key !== "form" || rule.form)}
                  onChange={(tab) => update({ tab })}
                />
                {tab === "form" && rule.form && (
                  <OaFormDesigner
                    client={client}
                    company={user.companyScope}
                    value={rule.form}
                    onChange={(form) => changeRule(index, { form })}
                  />
                )}
                {tab === "basic" && (
                  <>
                    <OfficeField label="流程名称">
                      <input
                        required
                        maxLength={100}
                        value={rule.name}
                        onChange={(event) => changeRule(index, { name: event.target.value })}
                      />
                    </OfficeField>
                    <p className="office-muted">
                      {publication
                        ? `当前发布版本 ${publication.version} · ${publication.enabled ? "已启用" : "已停用"}`
                        : "此流程尚未发布"}
                    </p>
                  </>
                )}
                {tab === "graph" && (
                  <ApprovalGraphEditor
                    key={rule.key}
                    definition={rule}
                    client={client}
                    currentCompanyCode={user.companyScope}
                    definitions={draft.rules}
                    stages={rule.stages}
                    accounts={accounts}
                    handlers={handlers}
                    units={units}
                    onChange={(stages) => changeRule(index, { stages })}
                  />
                )}
                {tab === "notify" && (
                  <OfficeField label="流程抄送（可查看进度，完成时通知）">
                    <select
                      multiple
                      value={rule.notifyUserIds.map(String)}
                      onChange={(event) =>
                        changeRule(index, {
                          notifyUserIds: Array.from(event.target.selectedOptions, (option) => Number(option.value)),
                        })
                      }
                    >
                      {accounts.map((account) => (
                        <option key={account.id} value={account.id}>
                          {account.fullName} · {account.username}
                        </option>
                      ))}
                    </select>
                  </OfficeField>
                )}
                {tab === "history" && (
                  <ApprovalDefinitionHistory
                    client={client}
                    definitionKey={rule.key}
                    onCopy={(definition) => changeRule(index, definition)}
                  />
                )}
                {tab === "publish" && (
                  <div className="office-card-actions">
                    <Button type="button" onClick={() => simulate(false)}>
                      模拟当前流程
                    </Button>
                    <Button type="button" onClick={() => simulate(true)}>
                      仿真并发布此流程
                    </Button>
                    {publication?.enabled && (
                      <Button type="button" onClick={() => void disable(rule.key)}>
                        停用新申请
                      </Button>
                    )}
                  </div>
                )}
              </section>
            )}
            {view === "definitions" && rule?.form && (
              <div className="office-card-actions">
                <Button onClick={() => addApplication(rule)}>
                  复制为新事项
                </Button>
                {!publication && (
                  <Button
                    variant="danger"
                    onClick={() => void remove(rule.key, () => update({ definition: null, tab: null }))}
                  >
                    删除未发布草稿
                  </Button>
                )}
              </div>
            )}
            {view === "calendar" && (
              <BusinessCalendarEditor
                dateInputs={calendarInputs}
                onDateInput={changeCalendarInput}
                disabled={operation.busy}
                value={draft.calendar}
                onChange={(calendar) => setDraft((value) => ({ ...value, calendar }))}
              />
            )}
            {view === "handling" && (
              <HandlingServicesEditor
                services={draft.handlingServices ?? []}
                accounts={handlers}
                disabled={operation.busy}
                onChange={(handlingServices) => setDraft((value) => ({ ...value, handlingServices }))}
              />
            )}
            {view === "templates" && (
              <ApprovalSheetTemplates
                client={client}
                settings={draft}
                disabled={operation.busy}
                onChange={(sheetTemplates) => setDraft((value) => ({ ...value, sheetTemplates }))}
              />
            )}
            {view === "delegations" && (
              <ApprovalDelegationsEditor
                value={draft.delegations}
                accounts={accounts}
                timeZone={user.businessTimeZone}
                onChange={(delegations) => setDraft((value) => ({ ...value, delegations }))}
              />
            )}
          </fieldset>
          {operation.error && <InlineNotice tone="error">{operation.error}</InlineNotice>}
          <OfficeSubmit
            busy={operation.busy}
            disabled={view === "definitions" && !rule}
            label={view === "definitions" ? "保存当前流程" : `保存${views.find((item) => item.key === view)?.label}`}
          >
            <Button type="button" disabled={operation.busy} onClick={() => void reload()}>
              重新载入审批设置
            </Button>
          </OfficeSubmit>
        </form>
      )}
      {view === "runtime" && (
        <>
          <UnitWorkflowTasksPanel client={client} user={user} accounts={accounts} />
          <WorkflowExceptionsPanel client={client} />
          <ApprovalActivationsPanel
            rules={draft.rules}
            activations={draft.activations}
            busy={operation.busy}
            onCancel={(entry) => void cancelActivation(entry)}
          />
        </>
      )}
      {simulation && (
        <ApprovalSimulationDialog
          client={client}
          user={user}
          rule={simulation.rule}
          busy={operation.busy}
          error={operation.error}
          onPublish={
            simulation.publish
              ? (samples, activateAt) => publish(simulation.rule.key, samples, activateAt, () => setSimulation(null))
              : undefined
          }
          onClose={() => setSimulation(null)}
        />
      )}
    </EditorWorkspace>
  );
}
