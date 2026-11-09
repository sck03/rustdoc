import { invoke, isTauri } from "@tauri-apps/api/core";
import type { ApiSingleWindowClientDirectories } from "../api/index.ts";

export type DetectedClientDirectories = {
  customsCooDirectories: ApiSingleWindowClientDirectories | null;
  agentConsignmentDirectories: ApiSingleWindowClientDirectories | null;
};

export async function detectSingleWindowClientDirectories() {
  if (!isTauri()) throw new Error("请在持卡机桌面程序检测目录，或手动填写。");
  return invoke<DetectedClientDirectories>("detect_single_window_client_directories");
}
