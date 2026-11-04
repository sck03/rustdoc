import type { HTMLAttributes, ReactNode } from "react";
import { PageHeader } from "../primitives/PageHeader.tsx";
type Props = HTMLAttributes<HTMLElement> & {
  title?: string; description?: string; actions?: ReactNode; children: ReactNode; label?: string;
};
function Workspace({ kind, title, description, actions, children, label, ...attributes }: Props & { kind: "list" | "record" | "editor" | "dashboard" }) {
  return <section {...attributes} data-ui="workspace" data-layout={kind} aria-label={label ?? attributes["aria-label"] ?? title}>
    {title && <PageHeader title={title} description={description} actions={actions} />}{children}
  </section>;
}
export function ListWorkspace(props: Props) { return <Workspace {...props} kind="list" />; }
export function RecordWorkspace(props: Props) { return <Workspace {...props} kind="record" />; }
export function EditorWorkspace(props: Props) { return <Workspace {...props} kind="editor" />; }
export function DashboardWorkspace(props: Props) { return <Workspace {...props} kind="dashboard" />; }
