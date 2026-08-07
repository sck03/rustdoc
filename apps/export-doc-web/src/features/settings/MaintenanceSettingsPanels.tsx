import { lazy, Suspense, useEffect, useState } from "react";
import { Activity, Database, FileWarning, LifeBuoy, Users } from "lucide-react";
import type { ApiHealthResponse } from "../../api/index.ts";
import { ExportDocManagerApiClient } from "../../api/index.ts";
import { RuntimeDiagnosticsSection } from "./RuntimeDiagnosticsSection.tsx";
import { PageState } from "../../ui/PageState.tsx";

const PostgreSqlMaintenancePanel = lazy(() =>
  import("./MaintenancePostgreSqlPanel.tsx")
    .then((module) => ({ default: module.PostgreSqlMaintenancePanel })));
const SupportPackagePanel = lazy(() =>
  import("./MaintenanceSupportPackagePanel.tsx")
    .then((module) => ({ default: module.SupportPackagePanel })));
const SharedDatabaseOwnershipPanel = lazy(() =>
  import("./MaintenanceOwnershipPanel.tsx")
    .then((module) => ({ default: module.SharedDatabaseOwnershipPanel })));
const DataOwnershipUnavailablePanel = lazy(() =>
  import("./MaintenanceInvoiceDataPanel.tsx")
    .then((module) => ({ default: module.DataOwnershipUnavailablePanel })));
const InvoiceDataMaintenancePanel = lazy(() =>
  import("./MaintenanceInvoiceDataPanel.tsx")
    .then((module) => ({ default: module.InvoiceDataMaintenancePanel })));

type MaintenanceSectionKey = "postgresql" | "ownership" | "invoice-cleanup" | "diagnostics" | "support";

export default function MaintenanceSettingsPanels({
  client,
  canManageSettings,
  canManageUsers,
  health,
  healthIsBusy,
  healthErrorMessage,
  initialPanelLabel,
  onRefreshHealth,
  onPathError,
}: {
  client: ExportDocManagerApiClient;
  canManageSettings: boolean;
  canManageUsers: boolean;
  health: ApiHealthResponse | null;
  healthIsBusy: boolean;
  healthErrorMessage: string | null;
  initialPanelLabel: string;
  onRefreshHealth: () => void;
  onPathError: (message: string) => void;
}) {
  const [activeSection, setActiveSection] = useState<MaintenanceSectionKey>("postgresql");
  const [technicalSectionsExpanded, setTechnicalSectionsExpanded] = useState(false);
  const sections = [
    { key: "postgresql" as const, label: "团队库", description: "备份与还原准备", icon: Database },
    { key: "ownership" as const, label: "数据归属", description: "人员变更时改派业务数据", icon: Users },
    { key: "invoice-cleanup" as const, label: "发票清理", description: "作废数据的审计维护", icon: FileWarning },
    { key: "diagnostics" as const, label: "运行检查", description: "检查目录和功能依赖", icon: Activity },
    { key: "support" as const, label: "问题诊断", description: "导出技术支持资料", icon: LifeBuoy },
  ];

  useEffect(() => {
    const normalizedLabel = initialPanelLabel.trim();
    if (!normalizedLabel) return;
    if (normalizedLabel.includes("运行诊断")) { setTechnicalSectionsExpanded(true); setActiveSection("diagnostics"); }
    else if (normalizedLabel.includes("支持") || normalizedLabel.includes("问题诊断")) { setTechnicalSectionsExpanded(true); setActiveSection("support"); }
    else if (normalizedLabel.includes("发票清理") || normalizedLabel.includes("数据清理")) setActiveSection("invoice-cleanup");
    else if (normalizedLabel.includes("归属") || normalizedLabel.includes("权限改派")) setActiveSection("ownership");
    else if (normalizedLabel.includes("PostgreSQL") || normalizedLabel.includes("团队库")) setActiveSection("postgresql");
  }, [canManageUsers, initialPanelLabel]);

  return (
    <div className="maintenance-workspace">
      <nav className="maintenance-section-nav" aria-label="维护工具分类">
        {sections.slice(0, 3).map((section) => {
          const Icon = section.icon;
          return (
            <button
              key={section.key}
              className={activeSection === section.key ? "maintenance-section-tab maintenance-section-tab-active" : "maintenance-section-tab"}
              type="button"
              aria-current={activeSection === section.key ? "page" : undefined}
              onClick={() => setActiveSection(section.key)}
            >
              <Icon size={18} aria-hidden="true" />
              <span><strong>{section.label}</strong><small>{section.description}</small></span>
            </button>
          );
        })}
        <details
          className="maintenance-technical-nav"
          open={technicalSectionsExpanded}
          onToggle={(event) => {
            const expanded = event.currentTarget.open;
            setTechnicalSectionsExpanded(expanded);
            if (!expanded && (activeSection === "diagnostics" || activeSection === "support")) {
              setActiveSection("postgresql");
            }
          }}
        >
          <summary><Activity size={18} aria-hidden="true" /><span><strong>高级支持与诊断</strong><small>低频技术信息，按需展开</small></span></summary>
          <div className="maintenance-technical-nav-items">
            {sections.slice(3).map((section) => {
              const Icon = section.icon;
              return (
                <button
                  key={section.key}
                  className={activeSection === section.key ? "maintenance-section-tab maintenance-section-tab-active" : "maintenance-section-tab"}
                  type="button"
                  aria-current={activeSection === section.key ? "page" : undefined}
                  onClick={() => { setTechnicalSectionsExpanded(true); setActiveSection(section.key); }}
                >
                  <Icon size={18} aria-hidden="true" />
                  <span><strong>{section.label}</strong><small>{section.description}</small></span>
                </button>
              );
            })}
          </div>
        </details>
      </nav>
      <Suspense fallback={<PageState tone="loading" title="正在加载维护面板" />}>
      <div className="maintenance-section-content">
        {activeSection === "postgresql" ? (
          <PostgreSqlMaintenancePanel client={client} canManageSettings={canManageSettings} onPathError={onPathError} />
        ) : null}
        {activeSection === "ownership" ? (
          canManageUsers
            ? <SharedDatabaseOwnershipPanel client={client} canManageUsers={canManageUsers} />
            : <DataOwnershipUnavailablePanel />
        ) : null}
        {activeSection === "invoice-cleanup" ? (
          <InvoiceDataMaintenancePanel client={client} canManageSettings={canManageSettings} />
        ) : null}
        {activeSection === "diagnostics" ? (
          <RuntimeDiagnosticsSection
            client={client}
            canManageSettings={canManageSettings}
            health={health}
            isBusy={healthIsBusy}
            errorMessage={healthErrorMessage}
            onRefresh={onRefreshHealth}
            onPathError={onPathError}
          />
        ) : null}
        {activeSection === "support" ? (
          <SupportPackagePanel client={client} canManageSettings={canManageSettings} onPathError={onPathError} />
        ) : null}
      </div>
      </Suspense>
    </div>
  );
}
