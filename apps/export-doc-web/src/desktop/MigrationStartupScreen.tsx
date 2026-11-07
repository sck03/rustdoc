import { useEffect, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { StartupScreen } from "../app/StartupScreen.tsx";

interface MigrationProgress {
  stage: string;
  completedBytes: number;
  totalBytes: number | null;
  error: string | null;
}

const stages: Record<string, string> = {
  prepare: "正在核验迁移数据…",
  copy: "正在复制数据…",
  verify: "正在校验副本…",
  switch: "正在切换数据目录…",
  cleanup: "正在整理原目录…",
  starting: "正在启动服务…",
  ready: "迁移完成",
};

export function MigrationStartupScreen() {
  const [progress, setProgress] = useState<MigrationProgress | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let active = true;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const poll = async () => {
      try {
        const next = await invoke<MigrationProgress>("get_data_root_migration_progress");
        if (!active) return;
        setProgress(next);
        if (!next.error && next.stage !== "ready") timer = setTimeout(() => void poll(), 300);
      } catch (cause) {
        if (active) setError(String(cause));
      }
    };
    void poll();
    return () => { active = false; clearTimeout(timer); };
  }, []);
  const total = progress?.totalBytes;
  return <StartupScreen name="数据目录迁移" error={error ?? progress?.error}
    message={stages[progress?.stage ?? "prepare"] ?? stages.prepare}
    progress={total && total > 0 ? { value: Math.min(progress!.completedBytes, total), max: total } : undefined}
  />;
}
