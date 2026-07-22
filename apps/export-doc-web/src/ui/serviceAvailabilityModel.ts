export type ServiceAvailability = "checking" | "available" | "unreachable";
export type ServiceConnectionState = "device-offline" | ServiceAvailability;

export function buildServiceReadinessUrl(apiBaseUrl: string) {
  const normalizedBaseUrl = `${apiBaseUrl.trim().replace(/\/+$/, "")}/`;
  return new URL("readyz", normalizedBaseUrl).toString();
}

export function resolveServiceConnectionState({
  isDesktopRuntime,
  isOnline,
  availability,
}: {
  isDesktopRuntime: boolean;
  isOnline: boolean;
  availability: ServiceAvailability;
}): ServiceConnectionState {
  return !isDesktopRuntime && !isOnline ? "device-offline" : availability;
}

export function getServiceConnectionLabel(state: ServiceConnectionState) {
  if (state === "device-offline") return "设备离线";
  if (state === "checking") return "正在检查服务";
  if (state === "unreachable") return "服务暂不可用";
  return "服务已连接";
}
