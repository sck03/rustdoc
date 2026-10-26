import type { ApiContainerPackingProjectDto } from "../../../api/index.ts";
import {
  type ContainerPackingCargoRow, type ContainerPackingFormState, type ContainerPackingRulesFormState,
  createContainerPackingCargoRow, formatFormNumber, normalizeContainerPackingZone, signedArgbToColorHex,
} from "./containerPackingModel.ts";

export type ContainerPackingProjectDraft = {
  projectName: string;
  container: ContainerPackingFormState;
  rules: ContainerPackingRulesFormState;
  cargoRows: ContainerPackingCargoRow[];
};
export const packingDraftFields = ["projectName", "container", "rules", "cargoRows"] as const;

export function createPackingProjectDraft(): ContainerPackingProjectDraft {
  return {
    projectName: "未命名方案",
    container: { containerType: "20GP", length: "589", width: "235", height: "239", volume: "33.2", maxWeight: "28000" },
    rules: {
      allowRotation: true, usePalletConstraints: false, defaultPalletLength: "120", defaultPalletWidth: "100",
      defaultPalletHeight: "15", defaultPalletWeight: "25", enforceCenterOfGravity: false,
      centerOfGravityTolerancePercent: "20", minimumSupportAreaPercent: "100", requireSameFootprintStacking: false,
    },
    cargoRows: [createContainerPackingCargoRow(0, {
      name: "纸箱", length: "60", width: "40", height: "40", weight: "10", quantity: "50", preferredZone: "Auto",
    })],
  };
}

export function packingDraftFromProject(project: ApiContainerPackingProjectDto): ContainerPackingProjectDraft {
  return {
    projectName: project.name || "未命名方案",
    container: {
      containerType: project.containerType || "", length: String(project.container.length || ""),
      width: String(project.container.width || ""), height: String(project.container.height || ""),
      volume: formatFormNumber(project.container.volume), maxWeight: formatFormNumber(project.container.maxWeight),
    },
    rules: {
      allowRotation: project.rules.allowRotation ?? true, usePalletConstraints: project.rules.usePalletConstraints ?? false,
      defaultPalletLength: String(project.rules.defaultPalletLength || 120), defaultPalletWidth: String(project.rules.defaultPalletWidth || 100),
      defaultPalletHeight: String(project.rules.defaultPalletHeight ?? 15), defaultPalletWeight: formatFormNumber(project.rules.defaultPalletWeight ?? 25),
      enforceCenterOfGravity: project.rules.enforceCenterOfGravity ?? false,
      centerOfGravityTolerancePercent: formatFormNumber(project.rules.centerOfGravityTolerancePercent ?? 20),
      minimumSupportAreaPercent: formatFormNumber(project.rules.minimumSupportAreaPercent ?? 100),
      requireSameFootprintStacking: project.rules.requireSameFootprintStacking ?? false,
    },
    cargoRows: project.cargoItems.length ? project.cargoItems.map((item, index) => createContainerPackingCargoRow(index, {
      name: item.name || `货物 ${index + 1}`, length: formatFormNumber(item.length), width: formatFormNumber(item.width),
      height: formatFormNumber(item.height), weight: formatFormNumber(item.weight), quantity: String(item.quantity || 1),
      colorHex: signedArgbToColorHex(item.colorArgb ?? -12417035), usePallet: item.usePallet,
      unitsPerPallet: String(item.unitsPerPallet || 1), maxTopLoadWeight: formatFormNumber(item.maxTopLoadWeight),
      preferredZone: normalizeContainerPackingZone(item.preferredZone), loadSequence: String(item.loadSequence || index + 1),
      priorityGroup: item.priorityGroup || "",
    })) : [createContainerPackingCargoRow(0)],
  };
}
