import type { ApiHealthResponse, ApiRuntimeDependencyInfo } from "../../api/index.ts";
import { normalizeRuntimeRequirement, type RuntimePathRequirement } from "./runtimeDiagnosticsModel.ts";

export type RuntimeDependencyStatus = "ready" | "missing" | "incomplete" | "disabled" | "unsupported" | "unknown";

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
    requirement: normalizeRuntimeRequirement(item.requirement),
    status: normalizeStatus(item.status),
  }));
}

export function summarizeRuntimeDependencies(items: RuntimeDependencyItem[]) {
  return {
    total: items.length,
    ready: items.filter((item) => item.ready).length,
    requiredUnavailable: items.filter((item) => item.requirement === "required" && !item.ready).length,
    optionalUnavailable: items.filter((item) => item.requirement === "optional" && !item.ready).length,
    invalidMetadata: items.filter(item => item.requirement === "unknown" || item.status === "unknown"
      || item.ready !== (item.status === "ready")).length,
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
    case "missing":
      return "未安装";
    default:
      return "状态未识别";
  }
}

function normalizeStatus(status: string): RuntimeDependencyStatus {
  switch (status) {
    case "ready": case "missing": case "incomplete": case "disabled": case "unsupported": return status;
    default: return "unknown";
  }
}
