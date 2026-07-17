export type SettingsRecord = Record<string, unknown>;

export type SettingPatch = {
  path: string[];
  value: unknown;
};
