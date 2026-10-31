import { useId, useState, type ReactNode } from "react";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { TaskViewPanel, TaskViewTabs } from "../../ui/TaskViewTabs.tsx";
import { PageState } from "../../ui/PageState.tsx";
import { UnitAccessPanel } from "./UnitAccessPanel.tsx";
import { UnitMembersPanel } from "./UnitMembersPanel.tsx";
import { UnitWorkflowSummary } from "./UnitWorkflowSummary.tsx";

export function OrganizationUnitWorkspace({ client, user, company, unitId, isGroup = false, departments }: {
  client: ExportDocManagerApiClient; user: ApiUserDto; company: string;
  unitId?: number; isGroup?: boolean; departments?: ReactNode;
}) {
  const prefix = useId();
  const [selected, setSelected] = useState<string | null>(null);
  const admin = user.capabilities.canManageUsers;
  const management = user.capabilities.unitManagement;
  const tabs = [
    { id: "departments", label: "公司与部门", available: Boolean(departments) },
    { id: "members", label: "任职与岗位", available: !isGroup && (admin || management?.appointments) },
    { id: "access", label: "单位身份与授权", available: admin || management?.roles },
    { id: "analytics", label: "审批统计", available: admin || management?.analytics },
  ].filter(tab => tab.available);
  const active = tabs.find(tab => tab.id === selected)?.id ?? tabs[0]?.id;
  if (!active) return <PageState tone="permission" title="当前身份没有单位管理权限" />;
  return <div className="organization-unit-workspace">
    <TaskViewTabs idPrefix={prefix} label="组织与任职分区" items={tabs} value={active} onChange={setSelected} />
    {tabs.map(tab => <TaskViewPanel key={tab.id} idPrefix={prefix} id={tab.id} active={active === tab.id}>
      {tab.id === "departments" && departments}
      {tab.id === "members" && <UnitMembersPanel client={client} user={user} company={company} />}
      {tab.id === "access" && <UnitAccessPanel client={client} user={user} company={company} />}
      {tab.id === "analytics" && <UnitWorkflowSummary client={client} user={user} unitId={admin ? unitId : undefined} />}
    </TaskViewPanel>)}
  </div>;
}
