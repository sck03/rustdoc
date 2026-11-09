import { useEffect, useRef, useState } from "react";
import { FolderOpen, RefreshCw } from "lucide-react";
import type { ApiSingleWindowClientDirectories } from "../../api/index.ts";
import { selectDirectory } from "../../desktop/desktopBridge.ts";
import { detectSingleWindowClientDirectories } from "../../desktop/singleWindowClient.ts";
import { DesktopIconButton, readDesktopError, renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { PathField } from "../../ui/PathField.tsx";

export const emptyClientDirectories = (): ApiSingleWindowClientDirectories => ({ outBox: "", sentBox: "", inBox: "", failBox: "" });
const boxes = [["outBox", "发送目录 OutBox"], ["sentBox", "已发送目录 SentBox"], ["inBox", "回执目录 InBox"], ["failBox", "失败目录 FailBox"]] as const;
type Directories = { customsCooDirectories: ApiSingleWindowClientDirectories; agentConsignmentDirectories: ApiSingleWindowClientDirectories };

export function SingleWindowClientDirectories({ value, onChange, disabled, autoDetect, cooEnabled, acdEnabled }: {
  value: Directories; onChange: (next: Directories) => void; disabled: boolean; autoDetect: boolean;
  cooEnabled: boolean; acdEnabled: boolean;
}) {
  const [message, setMessage] = useState<string | null>(null);
  const [detecting, setDetecting] = useState(false);
  const latest = useRef({ value, onChange, disabled });
  latest.current = { value, onChange, disabled };
  const request = useRef(0);

  async function detect(onlyEmpty: boolean) {
    const id = ++request.current;
    const before = JSON.stringify(latest.current.value);
    setDetecting(true);
    try {
      const found = await detectSingleWindowClientDirectories();
      if (id !== request.current || latest.current.disabled) return;
      if (JSON.stringify(latest.current.value) !== before) {
        setMessage("目录已手动修改，保留当前输入；需要时可重新检测。");
        return;
      }
      const next = { ...latest.current.value };
      for (const field of ["customsCooDirectories", "agentConsignmentDirectories"] as const) {
        if (!found[field]) continue;
        next[field] = { ...next[field] };
        for (const [box] of boxes) {
          if (!onlyEmpty || !next[field][box].trim()) next[field][box] = found[field][box];
        }
      }
      latest.current.onChange(next);
      setMessage(found.customsCooDirectories && found.agentConsignmentDirectories
        ? "已读取客户端任务目录，请核对后保存。"
        : "未找到全部业务的任务目录，请补选缺少的目录后保存。");
    } catch (error) {
      if (id === request.current) setMessage(readDesktopError(error));
    } finally {
      if (id === request.current) setDetecting(false);
    }
  }

  useEffect(() => {
    if (autoDetect && !latest.current.disabled) void detect(true);
    return () => { request.current += 1; };
  }, [autoDetect]);

  async function choose(field: keyof Directories, box: keyof ApiSingleWindowClientDirectories) {
    const id = request.current;
    try {
      const selected = await selectDirectory(latest.current.value[field][box]);
      if (selected && id === request.current && !latest.current.disabled) {
        latest.current.onChange({ ...latest.current.value, [field]: { ...latest.current.value[field], [box]: selected } });
      }
    } catch (error) { if (id === request.current) setMessage(readDesktopError(error)); }
  }

  return <section aria-label="官方客户端任务目录">
    <div className="section-header compact-section-header">
      <h3>官方客户端任务目录</h3>
      <button className="command-button secondary" type="button" disabled={disabled || detecting} onClick={() => void detect(false)}>
        <RefreshCw size={16} aria-hidden="true" /><span>{detecting ? "正在检测…" : "重新检测目录"}</span>
      </button>
    </div>
    {message ? <InlineNotice tone="info">{message}</InlineNotice> : null}
    {([["customsCooDirectories", "COO", cooEnabled], ["agentConsignmentDirectories", "ACD", acdEnabled]] as const).map(([field, label, enabled]) => <div key={field} className="field-grid">
      {boxes.map(([box, title]) => <PathField key={box} label={`${label} ${title}`} value={value[field][box]} disabled={disabled || !enabled}
        onChange={text => onChange({ ...value, [field]: { ...value[field], [box]: text } })}
        actions={<>
          <DesktopIconButton title={`选择 ${label} ${title}`} disabled={disabled || !enabled} onClick={() => void choose(field, box)}><FolderOpen size={17} aria-hidden="true" /></DesktopIconButton>
          {renderOpenPathAction(value[field][box], `打开 ${label} ${title}`, setMessage)}
        </>} />)}
    </div>)}
  </section>;
}
