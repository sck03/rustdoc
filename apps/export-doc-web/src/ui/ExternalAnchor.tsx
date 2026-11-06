import { useState, type ReactNode } from "react";
import { isDesktopBridgeAvailable, openExternalUrl } from "../desktop/desktopBridge.ts";
import { readApiError } from "./formUtils.ts";

export function ExternalAnchor({ href, children, className }: { href: string; children: ReactNode; className?: string }) {
  const [error, setError] = useState<string | null>(null);
  let url: URL;
  try { url = new URL(href); } catch { return <span>{children}</span>; }
  if (!["http:", "https:"].includes(url.protocol) || url.username || url.password) return <span>{children}</span>;
  return <><a href={url.href} className={className} target="_blank" rel="noopener noreferrer" onClick={event => {
    if (!isDesktopBridgeAvailable()) return;
    event.preventDefault();
    setError(null);
    void openExternalUrl(url.href).catch(error => setError(readApiError(error)));
  }}>{children}</a>{error && <span role="alert">{error}</span>}</>;
}
