import type { ReactNode } from "react";

export function TablePrimaryText({ value, secondary }: { value?: string | null; secondary?: ReactNode }) {
  const text = value?.trim() || "-";
  return <span className="table-primary-text-wrap">
    <span className="table-primary-text" title={text}>{text}</span>
    {secondary ? <small className="table-secondary-text">{secondary}</small> : null}
  </span>;
}
