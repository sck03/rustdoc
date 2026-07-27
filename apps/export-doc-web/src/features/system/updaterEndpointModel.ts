export function readUpdaterEndpoint(settings?: Record<string, unknown> | null) {
  if (!settings || typeof settings !== "object") {
    return "";
  }

  const system = settings.system;
  if (!system || typeof system !== "object") {
    return "";
  }

  const value = (system as Record<string, unknown>).updaterEndpoint;
  return typeof value === "string" ? value.trim() : "";
}

export function isInsecureHttpUpdaterEndpoint(endpoint: string) {
  return endpoint.trim().toLowerCase().startsWith("http://");
}

export function describeUpdaterEndpoint(endpoint: string) {
  return endpoint.trim() || "使用安装包默认地址";
}
