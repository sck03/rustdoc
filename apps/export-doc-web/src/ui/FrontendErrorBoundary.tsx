import { Component, ErrorInfo, ReactNode } from "react";
import { reportFrontendError } from "../desktop/frontendErrorLogger.ts";
import { PageState } from "./PageState.tsx";
import { Button } from "./Button.tsx";

type FrontendErrorBoundaryState = {
  hasError: boolean;
  incidentId: string;
};

export class FrontendErrorBoundary extends Component<{ children: ReactNode }, FrontendErrorBoundaryState> {
  public state: FrontendErrorBoundaryState = { hasError: false, incidentId: "" };

  public static getDerivedStateFromError() {
    return { hasError: true };
  }

  public componentDidCatch(error: Error, info: ErrorInfo) {
    const incidentId = createIncidentId();
    this.setState({ incidentId });
    void reportFrontendError(
      `[${incidentId}] ${error.message || "React component error"}`,
      "react.error-boundary",
      `${error.stack || ""}\n${info.componentStack || ""}`,
    );
  }

  private retryCurrentView = () => {
    this.setState({ hasError: false, incidentId: "" });
  };

  private reloadApplication = () => {
    window.location.reload();
  };

  public render() {
    if (this.state.hasError) {
      return <FrontendFatalErrorState
        incidentId={this.state.incidentId}
        onRetry={this.retryCurrentView}
        onReload={this.reloadApplication}
      />;
    }

    return this.props.children;
  }
}

export function FrontendFatalErrorState({ incidentId, onRetry, onReload }: {
  incidentId: string;
  onRetry: () => void;
  onReload: () => void;
}) {
  const description = incidentId
    ? `系统已记录异常编号 ${incidentId}。可先重试当前界面；如果问题持续，请重新加载并把异常编号提供给维护人员。未保存内容可能无法恢复。`
    : "系统正在记录异常。可先重试当前界面；如果问题持续，请重新加载程序界面。未保存内容可能无法恢复。";
  return <main className="work-surface">
    <PageState
      tone="error"
      title="页面出现异常"
      description={description}
      action={<div className="form-actions">
        <Button variant="secondary" onClick={onRetry}>重试当前界面</Button>
        <Button variant="primary" onClick={onReload}>重新加载程序界面</Button>
      </div>}
    />
  </main>;
}

function createIncidentId() {
  const timestamp = new Date().toISOString().replace(/\D/g, "").slice(0, 14);
  const suffix = Math.random().toString(36).slice(2, 7).toUpperCase();
  return `WEB-${timestamp}-${suffix}`;
}
