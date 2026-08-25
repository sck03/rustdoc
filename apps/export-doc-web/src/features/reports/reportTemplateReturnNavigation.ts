import type { Location } from "react-router-dom";

export type ReportTemplateReturnTarget = {
  path: string;
  label: string;
};

export function createReportTemplateReturnState(
  location: Pick<Location, "pathname" | "search">,
  label: string,
) {
  return {
    reportTemplateReturnTarget: {
      path: `${location.pathname}${location.search}`,
      label,
    },
  };
}

export function readReportTemplateReturnTarget(state: unknown): ReportTemplateReturnTarget | null {
  if (!isRecord(state) || !isRecord(state.reportTemplateReturnTarget)) return null;

  const path = state.reportTemplateReturnTarget.path;
  const label = state.reportTemplateReturnTarget.label;
  if (typeof path !== "string" || typeof label !== "string") return null;
  if (!/^\/(invoices|payments)\/(new|[1-9]\d*)(?:\?.*)?$/.test(path)) return null;
  return { path, label: label.trim() || "返回业务单据" };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
