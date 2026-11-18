import type { ApiHealthResponse, ApiRuntimePathInfo } from "../../api/index.ts";

export type RuntimePathAvailability = "available" | "missing";
export type RuntimePathRequirement = "required" | "optional" | "unknown";

export type RuntimePathItem = ApiRuntimePathInfo & {
  availability: RuntimePathAvailability;
  requirement: RuntimePathRequirement;
  storageClass: RuntimePathGroup["key"];
  invalidMetadata: boolean;
};

export type RuntimePathGroup = {
  key: "application" | "data" | "unknown";
  label: string;
  items: RuntimePathItem[];
};

const groupCatalog: Array<Omit<RuntimePathGroup, "items">> = [
  { key: "application", label: "程序文件" },
  { key: "data", label: "运行数据" },
  { key: "unknown", label: "未识别目录类型" },
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
  const coreItems = items.filter((item) => item.requirement === "required");
  return {
    total: items.length,
    coreTotal: coreItems.length,
    coreAvailable: coreItems.filter((item) => item.availability === "available").length,
    coreMissing: coreItems.filter((item) => item.availability === "missing").length,
    invalidMetadata: items.filter(item => item.invalidMetadata).length,
  };
}

export function runtimePathAccessModeLabel(accessMode: string) {
  switch (accessMode) {
    case "read":
      return "只读";
    case "read-write":
      return "运行时可写";
    default:
      return "访问方式未识别";
  }
}

export function runtimePathRequirementLabel(requirement: RuntimePathRequirement) {
  switch (requirement) {
    case "required":
      return "核心必需";
    case "optional":
      return "可选组件";
    default:
      return "必需性未识别";
  }
}

function normalizeRuntimePath(item: ApiRuntimePathInfo): RuntimePathItem {
  return {
    ...item,
    storageClass: item.storageClass === "application" || item.storageClass === "data" ? item.storageClass : "unknown",
    requirement: normalizeRuntimeRequirement(item.requirement),
    availability: item.exists ? "available" : "missing",
    invalidMetadata: !["application", "data"].includes(item.storageClass)
      || !["read", "read-write"].includes(item.accessMode) || normalizeRuntimeRequirement(item.requirement) === "unknown",
  };
}

export function normalizeRuntimeRequirement(requirement: string): RuntimePathRequirement {
  return requirement === "required" || requirement === "optional" ? requirement : "unknown";
}
