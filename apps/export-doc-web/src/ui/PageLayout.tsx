import type { ReactNode } from "react";

export function ListPageLayout({
  label,
  tabs,
  notice,
  toolbar,
  feedback,
  children,
  pagination,
}: {
  label: string;
  tabs?: ReactNode;
  notice?: ReactNode;
  toolbar?: ReactNode;
  feedback?: ReactNode;
  children: ReactNode;
  pagination?: ReactNode;
}) {
  return <section className="work-surface list-page-layout" aria-label={label}>
    {tabs}
    {notice}
    {toolbar}
    {feedback}
    <div className="list-page-content">{children}</div>
    {pagination}
  </section>;
}

export function EditorPageLayout({
  label,
  toolbar,
  feedback,
  notice,
  children,
}: {
  label: string;
  toolbar?: ReactNode;
  feedback?: ReactNode;
  notice?: ReactNode;
  children: ReactNode;
}) {
  return <section className="editor-surface editor-page-layout" aria-label={label}>
    {toolbar}
    {feedback}
    {notice}
    <div className="editor-page-content">{children}</div>
  </section>;
}
