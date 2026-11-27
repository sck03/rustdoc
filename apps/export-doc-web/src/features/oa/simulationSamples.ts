import type { OaSimulationSample } from "../../api/index.ts";

export interface PublicationSample { sample: OaSimulationSample; name: string }
export function addPublicationSample(current: PublicationSample[], sample: OaSimulationSample, name: string, catalogRevision: number): PublicationSample[] {
  const normalized = { ...sample, catalogRevision };
  const key = JSON.stringify(normalized);
  return current.some(item => JSON.stringify(item.sample) === key) || current.length >= 20
    ? current : [...current, { sample: normalized, name }];
}
