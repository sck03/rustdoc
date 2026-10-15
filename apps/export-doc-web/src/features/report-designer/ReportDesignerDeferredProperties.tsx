import { type ReactNode, useEffect, useState } from "react";

/** Create expensive editors on first visit, then retain their draft and selection state. */
export function VisitedPropertyPanel({ active, children }: { active: boolean; children: ReactNode }) {
  const [visited, setVisited] = useState(active);
  useEffect(() => { if (active) setVisited(true); }, [active]);
  return <div hidden={!active}>{active || visited ? children : null}</div>;
}

export function DeferredPropertySection({ title, children }: { title: string; children: ReactNode }) {
  const [visited, setVisited] = useState(false);
  return <details className="report-designer-property-section"
    onToggle={event => { if (event.currentTarget.open) setVisited(true); }}
    onInvalidCapture={event => { event.currentTarget.open = true; setVisited(true); }}>
    <summary>{title}</summary>{visited ? children : null}
  </details>;
}
