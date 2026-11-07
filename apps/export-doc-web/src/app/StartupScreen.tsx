import { FileSpreadsheet, Copy, RefreshCw } from "lucide-react";
import { Button } from "../ui/Button.tsx";
export function StartupScreen({ name = "工作空间", error, logPath, onRetry, message = "正在准备应用与连接服务…", progress }: { name?: string; error?: string | null; logPath?: string; onRetry?: () => void; message?: string; progress?: { value: number; max: number } }) {
  return <main data-ui="startup" aria-busy={!error}>
    <section data-ui="startup-card" role={error ? "alert" : "status"}>
      <FileSpreadsheet size={24} strokeWidth={1.75} aria-hidden="true" />
      {error && <p>{name}</p>}
      <h1>{error ? "启动未完成" : name}</h1>
      {error ? <><p>{error}</p>{logPath && <p>日志位置：<code>{logPath}</code></p>}
        <div><Button icon={<Copy size={18} aria-hidden="true" />} onClick={() => void navigator.clipboard.writeText([error, logPath].filter(Boolean).join("\n"))}>复制诊断信息</Button>
          {onRetry && <Button variant="primary" icon={<RefreshCw size={18} aria-hidden="true" />} onClick={onRetry}>重新尝试</Button>}</div>
      </> : <><p>{message}</p>{progress ? <><progress data-ui="startup-progress" aria-label={message} value={progress.value} max={progress.max} /><p>{Math.floor(progress.value / progress.max * 100)}%</p></> : <div data-ui="startup-progress" aria-hidden="true"><span /></div>}</>}
    </section>
  </main>;
}
