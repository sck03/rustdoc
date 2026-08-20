import type { ReactNode } from "react";

export function ResponsiveTableFrame({
  children,
  label,
  mobileLayout = "scroll",
  className = "",
  busy,
}: {
  children: ReactNode;
  label?: string;
  mobileLayout?: "scroll" | "cards";
  className?: string;
  busy?: boolean;
}) {
  const isScrollableRegion = mobileLayout === "scroll";

  return <div
    className={`table-frame responsive-table-frame ${className}`.trim()}
    data-mobile-layout={mobileLayout}
    role={isScrollableRegion ? "region" : undefined}
    aria-label={label}
    aria-busy={busy}
    tabIndex={isScrollableRegion ? 0 : undefined}
  >
    {children}
  </div>;
}
