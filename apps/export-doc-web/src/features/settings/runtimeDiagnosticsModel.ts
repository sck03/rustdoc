import type { ApiHealthResponse, ApiRuntimePathInfo } from "../../api/index.ts";

export type RuntimePathAvailability = "available" | "missing";
export type RuntimePathRequirement = "core" | "feature" | "optional";

export type RuntimePathItem = ApiRuntimePathInfo & {
  availability: RuntimePathAvailability;
  requirement: RuntimePathRequirement;
  storageClass: RuntimePathGroup["key"];
};

export type RuntimePathGroup = {
  key: "program-resource" | "runtime-data" | "database-file";
  label: string;
  description: string;
  items: RuntimePathItem[];
};

const groupCatalog: Array<Omit<RuntimePathGroup, "items">> = [
  {
    key: "program-resource",
    label: "程序与稳定资源",
    description: "随软件发布或由明确功能维护，不应被健康检查和普通浏览动作隐式创建。",
  },
  {
    key: "runtime-data",
    label: "运行数据",
    description: "数据库、日志、缓存、备份和业务文件统一跟随运行数据根，可整体迁移到非系统盘。",
  },
  {
    key: "database-file",
    label: "当前数据库",
    description: "SQLite 单机版使用运行数据根内的数据库文件；PostgreSQL 模式不会显示本地数据库文件。",
  },
];

export function buildRuntimePathGroups(health: ApiHealthResponse | null): RuntimePathGroup[] {
  if (!health) {
    return [];
  }

  const normalized = health.runtimePaths.map(normalizeRuntimePath);

  return groupCatalog
    .map((group) => ({
      ...group,
      items: normalized.filter((item) => item.storageClass === group.key),
    }))
    .filter((group) => group.items.length > 0);
}

export function summarizeRuntimePathGroups(groups: RuntimePathGroup[]) {
  const items = groups.flatMap((group) => group.items);
  const coreItems = items.filter((item) => item.requirement === "core");
  return {
    total: items.length,
    coreTotal: coreItems.length,
    coreAvailable: coreItems.filter((item) => item.availability === "available").length,
    coreMissing: coreItems.filter((item) => item.availability === "missing").length,
    featureMissing: items.filter((item) => item.requirement === "feature" && item.availability === "missing").length,
  };
}

export function runtimePathAccessModeLabel(accessMode: string) {
  switch (accessMode) {
    case "read-only":
      return "随程序只读";
    case "managed":
      return "功能维护";
    case "read-write":
      return "运行时可写";
    default:
      return accessMode?.trim() || "未说明";
  }
}

export function runtimePathRequirementLabel(requirement: RuntimePathRequirement) {
  switch (requirement) {
    case "core":
      return "核心必需";
    case "feature":
      return "按功能需要";
    default:
      return "可选组件";
  }
}

function normalizeRuntimePath(item: ApiRuntimePathInfo): RuntimePathItem {
  return {
    ...item,
    storageClass: item.storageClass as RuntimePathGroup["key"],
    requirement: item.requirement as RuntimePathRequirement,
    availability: item.exists ? "available" : "missing",
  };
}
