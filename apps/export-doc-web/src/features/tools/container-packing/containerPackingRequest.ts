import type { ApiContainerPackingAnalyzeRequest, ApiContainerPackingProjectSaveRequest } from "../../../api/index.ts";
import { ExactDecimal } from "../../../ui/decimalSum.ts";
import type { ContainerPackingCargoRow, ContainerPackingFormState, ContainerPackingRulesFormState } from "./containerPackingModel.ts";

// Keep invalid drafts intact. Analysis, project saves and type saves share these parsers.
function number(value: string, label: string, maximum: number, positive = false, integer = false) {
  const text = value.trim();
  const parsed = Number(text);
  if (text.length > 128 || !/^\+?\d+(?:\.\d+)?$/.test(text) || !Number.isFinite(parsed) ||
      parsed < 0 || (positive && parsed === 0) || parsed > maximum || (integer && !Number.isSafeInteger(parsed))) {
    throw new Error(`${label}须为${positive ? "大于 0" : "不小于 0"}且不超过 ${maximum} 的${integer ? "整数" : "数值"}。`);
  }
  if (ExactDecimal.from(text).compare(parsed) !== 0 || (text.split(".")[1]?.replace(/0+$/, "").length ?? 0) > 28) {
    throw new Error(`${label}精度超出当前数值范围，请减少小数位。`);
  }
  return parsed;
}

export function buildContainerPackingDimensions(container: ContainerPackingFormState) {
  return {
    length: number(container.length, "柜长", 100000, true, true),
    width: number(container.width, "柜宽", 100000, true, true),
    height: number(container.height, "柜高", 100000, true, true),
    volume: number(container.volume, "体积", 1000000000),
    maxWeight: number(container.maxWeight, "限重", 1000000000),
  };
}

function cargoItem(row: ContainerPackingCargoRow, index: number) {
  const label = (field: string) => `第 ${index + 1} 行${field}`;
  if ([...row.name.trim()].length > 200 || [...row.priorityGroup.trim()].length > 100) {
    throw new Error(`${label("名称或优先组")}过长。`);
  }
  if (!/^#[0-9a-f]{6}$/i.test(row.colorHex)) throw new Error(`${label("颜色")}无效。`);
  return {
    name: row.name.trim(),
    length: number(row.length, label("长度"), 100000, true),
    width: number(row.width, label("宽度"), 100000, true),
    height: number(row.height, label("高度"), 100000, true),
    weight: number(row.weight, label("重量"), 1000000000),
    quantity: number(row.quantity, label("数量"), 1000000, true, true),
    colorArgb: Number.parseInt(row.colorHex.slice(1), 16) | 0xff000000,
    usePallet: row.usePallet,
    unitsPerPallet: number(row.unitsPerPallet, label("每托数量"), 1000000, true, true),
    maxTopLoadWeight: number(row.maxTopLoadWeight, label("顶部承重"), 1000000000),
    preferredZone: row.preferredZone,
    loadSequence: number(row.loadSequence, label("顺序"), 2147483647, true, true),
    priorityGroup: row.priorityGroup.trim(),
  };
}

export function isValidContainerPackingCargoRow(row: ContainerPackingCargoRow) {
  try { cargoItem(row, 0); return true; } catch { return false; }
}

export function buildContainerPackingAnalyzeRequest(
  container: ContainerPackingFormState, cargoRows: ContainerPackingCargoRow[], rules: ContainerPackingRulesFormState,
): ApiContainerPackingAnalyzeRequest {
  const dimensions = buildContainerPackingDimensions(container);
  if (!cargoRows.length || cargoRows.length > 200) throw new Error("货物明细须为 1 至 200 行。");
  const cargoItems = cargoRows.map(cargoItem);
  const packages = cargoItems.reduce((sum, item) => sum + item.quantity, 0);
  const placements = cargoItems.reduce((sum, item) => sum + (rules.usePalletConstraints && item.usePallet
    ? Math.ceil(item.quantity / item.unitsPerPallet) : item.quantity), 0);
  if (packages > 1000000 || placements > 5000) throw new Error("货物最多 1000000 件、5000 个装载单元；请使用托盘约束或拆分方案。");
  if (!dimensions.volume) throw new Error("体积须大于 0，才能容纳当前货物。");
  if (!dimensions.maxWeight && (cargoItems.some(item => item.weight > 0) ||
      (rules.usePalletConstraints && cargoItems.some(item => item.usePallet) && Number(rules.defaultPalletWeight) > 0))) {
    throw new Error("限重须大于 0，才能容纳当前货物。");
  }
  return {
    container: dimensions, cargoItems,
    rules: {
      allowRotation: rules.allowRotation, usePalletConstraints: rules.usePalletConstraints,
      defaultPalletLength: number(rules.defaultPalletLength, "托盘长", 100000, true, true),
      defaultPalletWidth: number(rules.defaultPalletWidth, "托盘宽", 100000, true, true),
      defaultPalletHeight: number(rules.defaultPalletHeight, "托盘高", 100000, false, true),
      defaultPalletWeight: number(rules.defaultPalletWeight, "托盘重", 1000000000),
      enforceCenterOfGravity: rules.enforceCenterOfGravity,
      centerOfGravityTolerancePercent: number(rules.centerOfGravityTolerancePercent, "重心偏差", 100),
      minimumSupportAreaPercent: number(rules.minimumSupportAreaPercent, "支撑面积", 100),
      requireSameFootprintStacking: rules.requireSameFootprintStacking,
    },
  };
}

export function validatePackingInput<T>(build: () => T): { value: T | null; error: string | null } {
  try { return { value: build(), error: null }; }
  catch (error) { return { value: null, error: (error as Error).message }; }
}

export function buildContainerPackingProjectSaveRequest(
  projectId: number, expectedVersion: number, projectName: string, container: ContainerPackingFormState,
  cargoRows: ContainerPackingCargoRow[], rules: ContainerPackingRulesFormState,
): ApiContainerPackingProjectSaveRequest {
  if (!projectName.trim() || [...projectName.trim()].length > 200) throw new Error("方案名称须为 1 至 200 字。");
  if ([...container.containerType.trim()].length > 100) throw new Error("柜型名称不能超过 100 字。");
  return { id: projectId, expectedVersion, name: projectName.trim(), containerType: container.containerType.trim(),
    ...buildContainerPackingAnalyzeRequest(container, cargoRows, rules) };
}
