import type { ApiHealthResponse, ApiRuntimeDependencyInfo } from "../../api/index.ts";
import type { RuntimePathRequirement } from "./runtimeDiagnosticsModel.ts";

export type RuntimeDependencyStatus = "ready" | "missing" | "incomplete" | "disabled" | "unsupported";

export type RuntimeDependencyItem = ApiRuntimeDependencyInfo & {
  requirement: RuntimePathRequirement;
  status: RuntimeDependencyStatus;
};

export function buildRuntimeDependencyItems(health: ApiHealthResponse | null): RuntimeDependencyItem[] {
  if (!health) {
    return [];
  }

  return health.runtimeDependencies.map((item) => ({
    ...item,
    requirement: item.requirement as RuntimePathRequirement,
    status: item.status as RuntimeDependencyStatus,
  }));
}

export function summarizeRuntimeDependencies(items: RuntimeDependencyItem[]) {
  return {
    total: items.length,
    ready: items.filter((item) => item.ready).length,
    featureUnavailable: items.filter((item) => item.requirement === "feature" && !item.ready).length,
    optionalUnavailable: items.filter((item) => item.requirement === "optional" && !item.ready).length,
  };
}

export function runtimeDependencyStatusLabel(status: RuntimeDependencyStatus) {
  switch (status) {
    case "ready":
      return "已就绪";
    case "incomplete":
      return "文件不完整";
    case "disabled":
      return "已关闭";
    case "unsupported":
      return "当前平台不可用";
    default:
      return "未安装";
  }
}
