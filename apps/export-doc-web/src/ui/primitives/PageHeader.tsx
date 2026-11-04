import type { ReactNode, Ref } from "react";
export function PageHeader({ title, description, count, unit = "条", actions, children, selection, headingRef }: {
  title: string; description?: string; count?: number; unit?: string; actions?: ReactNode; children?: ReactNode; selection?: ReactNode; headingRef?: Ref<HTMLHeadingElement>;
}) {
  return <header data-ui="page-header">
    <div data-ui="page-heading"><div><div data-ui="page-title"><h2 ref={headingRef} tabIndex={headingRef ? -1 : undefined}>{title}</h2>{count !== undefined && <span>共 {count.toLocaleString("zh-CN")} {unit}</span>}</div>{description && <p>{description}</p>}</div>
      {actions && <div data-ui="page-actions">{actions}</div>}
    </div>
    {children && <div data-ui="page-query">{children}</div>}
    {selection && <div data-ui="page-selection" role="group" aria-label="所选记录操作">{selection}</div>}
  </header>;
}
