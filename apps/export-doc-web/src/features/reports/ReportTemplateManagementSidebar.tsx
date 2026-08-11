import type { ComponentProps } from "react";
import { ReportTemplateAdminPanel } from "./ReportTemplateAdminPanel.tsx";
import { ReportTemplatePackagePanel } from "./ReportTemplatePackagePanel.tsx";
import { ReportTemplateSelectionPanel } from "./ReportTemplateSelectionPanel.tsx";
import { ReportTemplateUserPanel } from "./ReportTemplateUserPanel.tsx";

type Props = {
  adminPanel: ComponentProps<typeof ReportTemplateAdminPanel>;
  packagePanel: ComponentProps<typeof ReportTemplatePackagePanel>;
  selectionPanel: ComponentProps<typeof ReportTemplateSelectionPanel>;
  userPanel: ComponentProps<typeof ReportTemplateUserPanel> | null;
};

export function ReportTemplateManagementSidebar({
  adminPanel,
  packagePanel,
  selectionPanel,
  userPanel,
}: Props) {
  return (
    <aside className="report-template-sidebar">
      <ReportTemplateSelectionPanel {...selectionPanel} />
      {userPanel ? <ReportTemplateUserPanel {...userPanel} /> : null}
      <ReportTemplateAdminPanel {...adminPanel} />
      <ReportTemplatePackagePanel {...packagePanel} />
    </aside>
  );
}
