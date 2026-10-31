import type { ReactNode } from "react";

export function ListWorkspaceHeader({ title, description, count, unit = "条", actions, children, selection }: {
  title: string; description?: string; count?: number; unit?: string;
  actions?: ReactNode; children?: ReactNode; selection?: ReactNode;
}) {
  return <div className="list-workspace-header">
    <div className="list-workspace-heading">
      <div><div className="list-workspace-title"><h2>{title}</h2>{count !== undefined && <span>共 {count.toLocaleString("zh-CN")} {unit}</span>}</div>
        {description && <p>{description}</p>}</div>
      {actions && <div className="list-workspace-actions">{actions}</div>}
    </div>
    {children && <div className="list-workspace-query">{children}</div>}
    {selection && <div className="list-workspace-selection" role="group" aria-label="所选记录操作">{selection}</div>}
  </div>;
}
