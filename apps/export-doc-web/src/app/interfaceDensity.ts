import { readStoredJson, writeStoredJson } from "../ui/browserStorage.ts";

export type InterfaceDensity = "comfortable" | "compact";

const interfaceDensityStorageKey = "exportdocmanager.interface-density";

export function readInterfaceDensity(): InterfaceDensity {
  return normalizeInterfaceDensity(readStoredJson<unknown>(interfaceDensityStorageKey));
}

export function applyInterfaceDensity(density: InterfaceDensity) {
  if (typeof document === "undefined") {
    return;
  }

  document.documentElement.dataset.interfaceDensity = density;
}

export function persistInterfaceDensity(density: InterfaceDensity) {
  applyInterfaceDensity(density);
  writeStoredJson(interfaceDensityStorageKey, density);
}

export function toggleInterfaceDensity(density: InterfaceDensity): InterfaceDensity {
  return density === "compact" ? "comfortable" : "compact";
}

function normalizeInterfaceDensity(value: unknown): InterfaceDensity {
  return value === "compact" ? "compact" : "comfortable";
}
