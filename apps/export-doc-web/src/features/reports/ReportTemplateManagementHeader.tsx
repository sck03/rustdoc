import { ArrowLeft, ArrowRight, RefreshCw } from "lucide-react";
import { Button, IconButton } from "../../ui/Button.tsx";
import type { ReportTemplateReturnTarget } from "./reportTemplateReturnNavigation.ts";

export function ReportTemplateManagementHeader({
  currentTemplateName,
  returnTarget,
  isBusy,
  canOpenDesigner,
  onRefresh,
  onOpenDesigner,
  onReturn,
}: {
  currentTemplateName: string;
  returnTarget: ReportTemplateReturnTarget | null;
  isBusy: boolean;
  canOpenDesigner: boolean;
  onRefresh: () => void;
  onOpenDesigner: () => void;
  onReturn: () => void;
}) {
  return (
    <div className="report-template-management-header">
      <div>
        <div className="report-template-management-title-row">
          {returnTarget ? (
            <Button
              variant="secondary"
              icon={<ArrowLeft size={17} aria-hidden="true" />}
              onClick={onReturn}
            >
              {returnTarget.label}
            </Button>
          ) : null}
          <h1>报表模板管理</h1>
        </div>
        <p>管理模板选择、默认值、生命周期和模板包。</p>
      </div>
      <div className="toolbar-actions">
        <span className="report-template-management-current" title={currentTemplateName}>
          当前：{currentTemplateName}
        </span>
        <IconButton label="刷新模板列表" disabled={isBusy} onClick={onRefresh}>
          <RefreshCw size={18} aria-hidden="true" />
        </IconButton>
        <Button
          variant="primary"
          icon={<ArrowRight size={17} aria-hidden="true" />}
          disabled={!canOpenDesigner}
          onClick={onOpenDesigner}
        >
          打开设计器
        </Button>
      </div>
    </div>
  );
}
