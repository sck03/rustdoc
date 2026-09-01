import type { ReactNode } from "react";
import type { WorkspaceDeviceMode } from "../../app/workspaceDevice.ts";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { WorkspaceDeviceNotice } from "../../ui/WorkspaceDeviceNotice.tsx";
import { ReportTemplateFeedback } from "./ReportTemplateFeedback.tsx";
import { ReportTemplateManagementHeader } from "./ReportTemplateManagementHeader.tsx";
import type { ReportTemplateReturnTarget } from "./reportTemplateReturnNavigation.ts";

export function ReportTemplateManagementView({
  currentTemplateName,
  returnTarget,
  workspaceDeviceMode,
  isBusy,
  canOpenDesigner,
  message,
  messageType,
  managementWorkspace,
  onRefresh,
  onOpenDesigner,
  onReturn,
}: {
  currentTemplateName: string;
  returnTarget: ReportTemplateReturnTarget | null;
  workspaceDeviceMode: WorkspaceDeviceMode;
  isBusy: boolean;
  canOpenDesigner: boolean;
  message: string | null;
  messageType: "success" | "error" | null;
  managementWorkspace: ReactNode;
  onRefresh: () => void;
  onOpenDesigner: () => void;
  onReturn: () => void;
}) {
  return (
    <section className="editor-surface report-template-surface report-template-management-surface" aria-label="报表模板管理">
      <form
        className="report-template-layout report-template-management-layout"
        onSubmit={(event) => event.preventDefault()}
        onKeyDownCapture={handleEnterAsTabFormKeyDown}
      >
        <ReportTemplateManagementHeader
          currentTemplateName={currentTemplateName}
          returnTarget={returnTarget}
          isBusy={isBusy}
          canOpenDesigner={canOpenDesigner}
          onRefresh={onRefresh}
          onOpenDesigner={onOpenDesigner}
          onReturn={onReturn}
        />
        <WorkspaceDeviceNotice
          mode={workspaceDeviceMode}
          phone="可选择和维护模板；V3 可视化设计请使用平板或桌面端。"
          tablet="可管理模板和模板包；完整可视化设计建议使用更宽的桌面窗口。"
        />
        <ReportTemplateFeedback message={message} type={messageType} />
        {managementWorkspace}
      </form>
    </section>
  );
}
