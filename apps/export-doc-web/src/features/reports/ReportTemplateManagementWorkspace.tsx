import type { ComponentProps } from "react";
import { ReportTemplateAdminPanel } from "./ReportTemplateAdminPanel.tsx";
import { ReportTemplatePackagePanel } from "./ReportTemplatePackagePanel.tsx";
import { ReportTemplateFilePanel } from "./ReportTemplateFilePanel.tsx";
import { ReportExportDefaultsPanel } from "./ReportExportDefaultsPanel.tsx";
import { ReportTemplateSelectionPanel } from "./ReportTemplateSelectionPanel.tsx";
import { ReportTemplateUserPanel } from "./ReportTemplateUserPanel.tsx";

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
  return (
    <div className="report-template-management-workspace">
      <ReportTemplateSelectionPanel {...selectionPanel} />
      {reportType === "ExportDocument" ? <ReportExportDefaultsPanel {...exportDefaultsPanel} /> : null}
      {userPanel ? <ReportTemplateUserPanel {...userPanel} /> : null}
      <ReportTemplateAdminPanel {...adminPanel} />
      <ReportTemplatePackagePanel {...packagePanel} />
      <ReportTemplateFilePanel {...filePanel} />
    </div>
  );
}
