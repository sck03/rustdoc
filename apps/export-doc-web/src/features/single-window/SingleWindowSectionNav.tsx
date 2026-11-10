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
        <button className="single-window-section-nav-item" type="button" key={item.id}
          aria-controls={item.id}
          onClick={() => {
            const target = document.getElementById(item.id);
            if (!target) return;
            for (let element: HTMLElement | null = target; element; element = element.parentElement) {
              if (element instanceof HTMLDetailsElement) element.open = true;
            }
            target.scrollIntoView({ block: "start", behavior: "auto" });
          }}>
          <span>{item.label}</span>
          {item.badge ? <strong>{item.badge}</strong> : null}
        </button>
      ))}
    </nav>
  );
}
