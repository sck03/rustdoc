import { Component, ErrorInfo, ReactNode } from "react";
import { reportFrontendError } from "../desktop/frontendErrorLogger.ts";

export class FrontendErrorBoundary extends Component<{ children: ReactNode }, { hasError: boolean }> {
  public state = { hasError: false };

  public static getDerivedStateFromError() {
    return { hasError: true };
  }

  public componentDidCatch(error: Error, info: ErrorInfo) {
    void reportFrontendError(
      error.message || "React component error",
      "react.error-boundary",
      `${error.stack || ""}\n${info.componentStack || ""}`,
    );
  }

  public render() {
    if (this.state.hasError) {
      return <div className="loading-panel">页面出现异常，请导出支持包并联系售后。</div>;
    }

    return this.props.children;
  }
}
