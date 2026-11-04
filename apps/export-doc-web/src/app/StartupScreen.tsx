import { FileSpreadsheet, Copy, RefreshCw } from "lucide-react";
export function StartupScreen({ name = "工作空间", error, logPath, onRetry }: { name?: string; error?: string | null; logPath?: string; onRetry?: () => void }) {
  return <main data-ui="startup" aria-busy={!error}>
    <section data-ui="startup-card" role={error ? "alert" : "status"}>
      <FileSpreadsheet size={32} strokeWidth={1.75} aria-hidden="true" />
      {error && <p>{name}</p>}
      <h1>{error ? "启动未完成" : name}</h1>
      {error ? <><p>{error}</p>{logPath && <p>日志位置：<code>{logPath}</code></p>}
        <div><button className="command-button secondary" type="button" onClick={() => void navigator.clipboard.writeText([error, logPath].filter(Boolean).join("\n"))}><Copy size={18} aria-hidden="true" />复制诊断信息</button>
          {onRetry && <button className="command-button" type="button" onClick={onRetry}><RefreshCw size={18} aria-hidden="true" />重新尝试</button>}</div>
      </> : <><p>正在准备应用与连接服务…</p><div data-ui="startup-progress" aria-hidden="true"><span /></div></>}
    </section>
  </main>;
}
