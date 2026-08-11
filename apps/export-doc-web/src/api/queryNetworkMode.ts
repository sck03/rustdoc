export type QueryNetworkMode = "always" | "online";

export function resolveQueryNetworkMode(desktopBridgeAvailable: boolean): QueryNetworkMode {
  return desktopBridgeAvailable ? "always" : "online";
}
