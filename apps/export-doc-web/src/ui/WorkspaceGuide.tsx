import type { ReactNode } from "react";

export function WorkspaceGuide({ title, children }: { title: string; children: ReactNode }) {
  return <details className="workspace-start-guide">
    <summary><strong>{title}</strong><span>查看准备步骤</span></summary>
    {children}
  </details>;
}
