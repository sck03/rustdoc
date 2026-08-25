import { useCallback } from "react";
import { useNavigate } from "react-router-dom";
import type { ConfirmationRequest } from "../../ui/ConfirmationProvider.tsx";
import type { ReportTypeOption } from "./reportTemplateDesignerModel.ts";
import type { ReportTemplateReturnTarget } from "./reportTemplateReturnNavigation.ts";
import { fileNameFromPath } from "./reportTemplateDesignerModel.ts";

export function useReportTemplateWorkspaceNavigation({
  reportType,
  selectedTemplatePath,
  selectedUserTemplateId,
  locationState,
  returnTarget,
  confirmDiscardChanges,
  requestConfirmation,
  exportDefaultsDirty,
  refetchTemplates,
}: {
  reportType: ReportTypeOption;
  selectedTemplatePath: string;
  selectedUserTemplateId: number;
  locationState: unknown;
  returnTarget: ReportTemplateReturnTarget | null;
  confirmDiscardChanges: (actionLabel?: string) => Promise<boolean>;
  requestConfirmation: (request: ConfirmationRequest) => Promise<boolean>;
  exportDefaultsDirty: boolean;
  refetchTemplates: () => Promise<unknown>;
}) {
  const navigate = useNavigate();
  const buildTemplateWorkspaceLocation = useCallback((pathname: string) => {
    const params = new URLSearchParams({ reportType });
    if (selectedUserTemplateId > 0) params.set("userTemplateId", String(selectedUserTemplateId));
    else if (selectedTemplatePath) params.set("template", fileNameFromPath(selectedTemplatePath));
    return `${pathname}?${params.toString()}`;
  }, [reportType, selectedTemplatePath, selectedUserTemplateId]);
  const handleRefreshTemplates = useCallback(async () => {
    if (await confirmDiscardChanges("刷新报表模板")) await refetchTemplates();
  }, [confirmDiscardChanges, refetchTemplates]);
  const handleBackToManagement = useCallback(async () => {
    if (await confirmDiscardChanges("返回模板管理")) {
      navigate(buildTemplateWorkspaceLocation("/reports/templates/manage"), { state: locationState });
    }
  }, [buildTemplateWorkspaceLocation, confirmDiscardChanges, locationState, navigate]);
  const handleReturnToBusiness = useCallback(async () => {
    if (!returnTarget || !await confirmDiscardChanges("返回业务单据")) return;
    if (exportDefaultsDirty && !await requestConfirmation({
      title: "返回业务单据",
      description: "导出默认设置有未保存修改，确定放弃这些修改并返回吗？",
      confirmLabel: "放弃并返回",
    })) return;
    navigate(returnTarget.path, { replace: true });
  }, [confirmDiscardChanges, exportDefaultsDirty, navigate, requestConfirmation, returnTarget]);
  const handleOpenDesigner = useCallback(() => {
    navigate(buildTemplateWorkspaceLocation("/reports/templates"), { state: locationState });
  }, [buildTemplateWorkspaceLocation, locationState, navigate]);
  return { handleRefreshTemplates, handleBackToManagement, handleReturnToBusiness, handleOpenDesigner };
}
