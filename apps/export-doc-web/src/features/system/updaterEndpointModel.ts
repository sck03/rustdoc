export function readUpdaterEndpoint(settings?: object | null) {
  if (!settings || typeof settings !== "object") {
    return "";
  }

  const system = (settings as { system?: unknown }).system;
  if (!system || typeof system !== "object") {
    return "";
  }

  const value = (system as Record<string, unknown>).updaterEndpoint;
  return typeof value === "string" ? value.trim() : "";
}
