import type { ComponentProps } from "react";
import { ReportTemplateAdminPanel } from "./ReportTemplateAdminPanel.tsx";
import { ReportTemplatePackagePanel } from "./ReportTemplatePackagePanel.tsx";
import { ReportTemplateFilePanel } from "./ReportTemplateFilePanel.tsx";
import { ReportExportDefaultsPanel } from "./ReportExportDefaultsPanel.tsx";
import { ReportTemplateSelectionPanel } from "./ReportTemplateSelectionPanel.tsx";
import { ReportTemplateUserPanel } from "./ReportTemplateUserPanel.tsx";
import { TaskViewTabs, getTaskViewPanelProps } from "../../ui/TaskViewTabs.tsx";
import { useRouteQuery } from "../../ui/useRouteQuery.ts";
import { readRouteChoice } from "../../ui/routeQueryState.ts";

type Props = {
  reportType: "ExportDocument" | "PaymentVoucher";
  adminPanel: ComponentProps<typeof ReportTemplateAdminPanel>;
  exportDefaultsPanel: ComponentProps<typeof ReportExportDefaultsPanel>;
  packagePanel: ComponentProps<typeof ReportTemplatePackagePanel>;
  filePanel: ComponentProps<typeof ReportTemplateFilePanel>;
  selectionPanel: ComponentProps<typeof ReportTemplateSelectionPanel>;
  userPanel: ComponentProps<typeof ReportTemplateUserPanel> | null;
};

export function ReportTemplateManagementWorkspace({
  reportType,
  adminPanel,
  exportDefaultsPanel,
  packagePanel,
  filePanel,
  selectionPanel,
  userPanel,
}: Props) {
  const { params, update } = useRouteQuery();
  const canTransfer = packagePanel.canImportTemplates || packagePanel.canExportTemplates;
  const tabs = [{ id: "catalog", label: "模板目录" },
    ...(reportType === "ExportDocument" ? [{ id: "defaults", label: "输出默认值" }] : []),
    ...(canTransfer ? [{ id: "transfer", label: "导入导出" }] : []),
  ];
  const view = readRouteChoice(params.get("view"), tabs.map((item) => item.id), "catalog");
  return (
    <div className="report-template-management-workspace">
      <TaskViewTabs idPrefix="report-management" label="报表模板管理视图" items={tabs} value={view} onChange={(next) => update({ view: next }, false)} />
      <ReportTemplateSelectionPanel {...selectionPanel} />
      <div hidden={view !== "catalog"} {...getTaskViewPanelProps("report-management", "catalog")}>
        {userPanel ? <ReportTemplateUserPanel {...userPanel} /> : null}
        <ReportTemplateAdminPanel {...adminPanel} />
      </div>
      {reportType === "ExportDocument" && <div hidden={view !== "defaults"} {...getTaskViewPanelProps("report-management", "defaults")}><ReportExportDefaultsPanel {...exportDefaultsPanel} /></div>}
      {canTransfer && <div hidden={view !== "transfer"} {...getTaskViewPanelProps("report-management", "transfer")}>
        <ReportTemplatePackagePanel {...packagePanel} /><ReportTemplateFilePanel {...filePanel} />
      </div>}
    </div>
  );
}
