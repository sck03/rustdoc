import type { ReactNode } from "react";

export type SingleWindowSectionNavItem = {
  id: string;
  label: string;
  badge?: ReactNode;
};

export function SingleWindowSectionNav({
  items,
  ariaLabel = "单一窗口录入分区",
}: {
  items: readonly SingleWindowSectionNavItem[];
  ariaLabel?: string;
}) {
  return (
    <nav className="single-window-section-nav" aria-label={ariaLabel}>
      {items.map((item) => (
        <a className="single-window-section-nav-item" href={`#${item.id}`} key={item.id}>
          <span>{item.label}</span>
          {item.badge ? <strong>{item.badge}</strong> : null}
        </a>
      ))}
    </nav>
  );
}
