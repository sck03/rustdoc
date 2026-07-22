import type { ReactNode } from "react";
import { AlertCircle, CheckCircle2, History, Inbox, Info, LoaderCircle, ShieldAlert, TriangleAlert } from "lucide-react";
import { Button } from "./Button.tsx";

export function PageState({
  tone = "empty",
  title,
  description,
  action,
}: {
  tone?: "loading" | "empty" | "error" | "permission";
  title: string;
  description?: string;
  action?: ReactNode;
}) {
  const Icon = tone === "loading" ? LoaderCircle
    : tone === "error" ? AlertCircle
      : tone === "permission" ? ShieldAlert
        : Inbox;
  return (
    <div
      className="page-state"
      data-tone={tone}
      role={tone === "error" ? "alert" : "status"}
      aria-live={tone === "error" ? "assertive" : "polite"}
      aria-busy={tone === "loading"}
    >
      <Icon className={tone === "loading" ? "page-state-spinner" : undefined} size={24} aria-hidden="true" />
      <strong>{title}</strong>
      {description ? <span>{description}</span> : null}
      {action ? <div className="page-state-action">{action}</div> : null}
    </div>
  );
}

export function PermissionNotice({ children }: { children: ReactNode }) {
  return <div className="permission-readonly-notice" role="status" aria-live="polite">
    <ShieldAlert size={17} aria-hidden="true" />
    <span>{children}</span>
  </div>;
}

export function ConcurrencyConflictNotice({ message, isBusy = false, onReload }: {
  message: string;
  isBusy?: boolean;
  onReload: () => void;
}) {
  return <div className="concurrency-conflict-notice" role="alert" aria-live="assertive">
    <History size={19} aria-hidden="true" />
    <div>
      <strong>这条数据已在其他位置更新</strong>
      <span>{message}</span>
      <small>为避免覆盖其他人的修改，请加载服务器上的最新版本后再继续。</small>
    </div>
    <Button variant="secondary" disabled={isBusy} onClick={onReload}>{isBusy ? "正在加载…" : "加载最新版本"}</Button>
  </div>;
}

export function FormGuidance({ title, description, action, className = "" }: {
  title: string;
  description: string;
  action?: ReactNode;
  className?: string;
}) {
  return <div className={`form-guidance ${className}`.trim()} role="note">
    <Inbox size={19} aria-hidden="true" />
    <div>
      <strong>{title}</strong>
      <span>{description}</span>
      {action ? <div className="form-guidance-action">{action}</div> : null}
    </div>
  </div>;
}

export function InlineNotice({
  tone,
  children,
  title,
  action,
  className = "",
}: {
  tone: "error" | "success" | "warning" | "info";
  children: ReactNode;
  title?: string;
  action?: ReactNode;
  className?: string;
}) {
  const Icon = tone === "error" ? AlertCircle
    : tone === "success" ? CheckCircle2
      : tone === "warning" ? TriangleAlert
        : Info;
  const isUrgent = tone === "error";

  return <div
    className={`inline-notice ${className}`.trim()}
    data-tone={tone}
    role={isUrgent ? "alert" : "status"}
    aria-live={isUrgent ? "assertive" : "polite"}
  >
    <Icon size={18} aria-hidden="true" />
    <div className="inline-notice-content">
      {title ? <strong>{title}</strong> : null}
      <div className="inline-notice-message">{children}</div>
    </div>
    {action ? <div className="inline-notice-action">{action}</div> : null}
  </div>;
}
