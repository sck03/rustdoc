import type { Location } from "react-router-dom";

type SettingsReturnTarget = { path: string; label: string };

export function createSettingsReturnState(location: Pick<Location, "pathname" | "search">, label: string) {
  return {
    settingsReturnTarget: {
      path: `${location.pathname}${location.search}`,
      label,
    },
  };
}

export function readSettingsReturnTarget(state: unknown): SettingsReturnTarget | null {
  if (!isRecord(state) || !isRecord(state.settingsReturnTarget)) return null;
  const path = state.settingsReturnTarget.path;
  const label = state.settingsReturnTarget.label;
  if (typeof path !== "string" || typeof label !== "string") return null;
  if (!/^\/(invoices|payments)\/(new|[1-9]\d*)(?:\?.*)?$/.test(path)) return null;
  return { path, label: label.trim() || "返回业务单据" };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
