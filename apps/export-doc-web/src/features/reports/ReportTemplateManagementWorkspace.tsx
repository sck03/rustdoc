import type { ComponentProps } from "react";
import { ReportTemplateAdminPanel } from "./ReportTemplateAdminPanel.tsx";
import { ReportTemplatePackagePanel } from "./ReportTemplatePackagePanel.tsx";
import { ReportExportDefaultsPanel } from "./ReportExportDefaultsPanel.tsx";
import { ReportTemplateSelectionPanel } from "./ReportTemplateSelectionPanel.tsx";
import { ReportTemplateUserPanel } from "./ReportTemplateUserPanel.tsx";

type Props = {
  adminPanel: ComponentProps<typeof ReportTemplateAdminPanel>;
  exportDefaultsPanel: ComponentProps<typeof ReportExportDefaultsPanel>;
  packagePanel: ComponentProps<typeof ReportTemplatePackagePanel>;
  selectionPanel: ComponentProps<typeof ReportTemplateSelectionPanel>;
  userPanel: ComponentProps<typeof ReportTemplateUserPanel> | null;
};

export function ReportTemplateManagementWorkspace({
  adminPanel,
  exportDefaultsPanel,
  packagePanel,
  selectionPanel,
  userPanel,
}: Props) {
  return (
    <div className="report-template-management-workspace">
      <ReportTemplateSelectionPanel {...selectionPanel} />
      <ReportExportDefaultsPanel {...exportDefaultsPanel} />
      {userPanel ? <ReportTemplateUserPanel {...userPanel} /> : null}
      <ReportTemplateAdminPanel {...adminPanel} />
      <ReportTemplatePackagePanel {...packagePanel} />
    </div>
  );
}
