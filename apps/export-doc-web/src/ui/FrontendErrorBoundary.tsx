import { Component, ErrorInfo, ReactNode } from "react";
import { reportFrontendError } from "../desktop/frontendErrorLogger.ts";
import { PageState } from "./PageState.tsx";

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
      return <PageState tone="error" title="页面出现异常" description="请导出支持包并联系售后；重新进入页面前，未保存内容可能无法恢复。" />;
    }

    return this.props.children;
  }
}
