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
  return <div
    className={`table-frame responsive-table-frame ${className}`.trim()}
    data-mobile-layout={mobileLayout}
    aria-label={label}
    aria-busy={busy}
    tabIndex={0}
  >
    {children}
  </div>;
}
