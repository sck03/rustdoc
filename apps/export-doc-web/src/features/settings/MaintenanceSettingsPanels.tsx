import { lazy, Suspense, type ReactNode } from "react";
import type { ApiHealthResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { RuntimeDiagnosticsSection } from "./RuntimeDiagnosticsSection.tsx";
import { PageState } from "../../ui/PageState.tsx";
import { useRouteQuery } from "../../ui/useRouteQuery.ts";
import { SegmentedNav } from "../../ui/primitives/Tabs.tsx";
import { maintenanceSections, operationSection, type MaintenanceSection } from "./systemOperationNavigation.ts";

const SupportPackagePanel = lazy(() => import("./MaintenanceSupportPackagePanel.tsx").then(module => ({ default: module.SupportPackagePanel })));
const SharedDatabaseOwnershipPanel = lazy(() => import("./MaintenanceOwnershipPanel.tsx").then(module => ({ default: module.SharedDatabaseOwnershipPanel })));
const DataOwnershipUnavailablePanel = lazy(() => import("./MaintenanceInvoiceDataPanel.tsx").then(module => ({ default: module.DataOwnershipUnavailablePanel })));
const InvoiceDataMaintenancePanel = lazy(() => import("./MaintenanceInvoiceDataPanel.tsx").then(module => ({ default: module.InvoiceDataMaintenancePanel })));

export default function MaintenanceSettingsPanels({ client, canManageSettings, canManageUsers, canUseDocumentWorkspace,
  logPanel, jobsPanel, health, healthIsBusy, healthErrorMessage, onRefreshHealth, onPathError }: {
  client: ExportDocManagerApiClient; canManageSettings: boolean; canManageUsers: boolean; canUseDocumentWorkspace: boolean;
  logPanel: ReactNode; jobsPanel: ReactNode; health: ApiHealthResponse | null; healthIsBusy: boolean;
  healthErrorMessage: string | null; onRefreshHealth: () => void; onPathError: (message: string) => void;
}) {
  const { params, update } = useRouteQuery();
  const requested = operationSection(params.toString(), "maintenance") as MaintenanceSection;
  const activeSection = requested === "invoice-cleanup" && !canUseDocumentWorkspace ? "diagnostics" : requested;
  return <div className="maintenance-workspace">
    <SegmentedNav label="系统维护分类" value={activeSection}
      items={maintenanceSections.filter(section => section.key !== "invoice-cleanup" || canUseDocumentWorkspace)}
      onChange={section => update({ section }, false)} />
    <Suspense fallback={<PageState tone="loading" title="正在加载维护面板" />}>
      {activeSection === "logs" && logPanel}
      {activeSection === "jobs" && jobsPanel}
      {activeSection === "ownership" && (canManageUsers ? <SharedDatabaseOwnershipPanel client={client} canManageUsers={canManageUsers} /> : <DataOwnershipUnavailablePanel />)}
      {activeSection === "invoice-cleanup" && canUseDocumentWorkspace && <InvoiceDataMaintenancePanel client={client} canManageSettings={canManageSettings} />}
      {activeSection === "diagnostics" && <RuntimeDiagnosticsSection client={client} canManageSettings={canManageSettings}
        health={health} isBusy={healthIsBusy} errorMessage={healthErrorMessage} onRefresh={onRefreshHealth} onPathError={onPathError} />}
      {activeSection === "support" && <SupportPackagePanel client={client} canManageSettings={canManageSettings} onPathError={onPathError} />}
    </Suspense>
  </div>;
}
